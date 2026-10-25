using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using RailWorld.src.RailWay;

namespace RailWorld
{
    /// <summary>
    /// Малює каркас навколо деталі секції, на яку дивиться гравець, а поки в руці шпала, рейка чи предмет
    /// для прокладання колії, ще й залиті кольором порожні місця під деталі, як гра підсвічує зони і заготовки споруд.
    /// Порожні місця кожного чанка склеєні в готові меші, які перебудовуються лише коли змінилися дані чанка.
    /// Саму деталь під прицілом шукає SectionInteractionClient.
    /// </summary>
    public class RailSelectionRenderer : IRenderer, IDisposable
    {

        private static readonly Vec4f SelectionColor = new Vec4f(0f, 0f, 0f, 0.5f);
        private static readonly Vec4f InstallableOutlineColor = new Vec4f(0.3f, 1f, 0.3f, 0.9f);
        private static readonly Vec4f WholeSectionColor = new Vec4f(1f, 0.3f, 0.3f, 0.9f);

        // Кольори заливки: червоний, зелений, синій, непрозорість. Меш заливки білий, колір дає шейдер railfill
        private static readonly Vec4f GhostTint = Tint(255, 255, 255, 60);
        private static readonly Vec4f InstallableTint = Tint(70, 255, 70, 90);
        private static readonly Vec4f BlockedTint = Tint(255, 60, 60, 110);
        private static readonly Vec4f NoTint = new Vec4f(1, 1, 1, 1);
        private static readonly int[] WhiteRgba = { 255, 255, 255, 255 };
        private static readonly int[] SelectedRgba = { 70, 255, 70, 150 };

        private static Vec4f Tint(int r, int g, int b, int a)
        {
            return new Vec4f(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        // Чотири кути кожної грані куба від -1 до 1 в осях боксу (бік, верх, уздовж) і яскравість грані
        private static readonly float[][] Faces =
        {
            new float[] {  1,-1,-1,   1, 1,-1,   1, 1, 1,   1,-1, 1 },
            new float[] { -1,-1, 1,  -1, 1, 1,  -1, 1,-1,  -1,-1,-1 },
            new float[] { -1, 1,-1,  -1, 1, 1,   1, 1, 1,   1, 1,-1 },
            new float[] { -1,-1, 1,  -1,-1,-1,   1,-1,-1,   1,-1, 1 },
            new float[] {  1,-1, 1,   1, 1, 1,  -1, 1, 1,  -1,-1, 1 },
            new float[] { -1,-1,-1,  -1, 1,-1,   1, 1,-1,   1,-1,-1 }
        };
        private static readonly float[] FaceShading = { 0.8f, 0.8f, 1f, 0.5f, 0.65f, 0.65f };

        // Заливка порожніх місць одного чанка: по мешу на групу, білому; білим чи зеленим його робить колір,
        // з яким його малюють
        private class ChunkFill
        {
            public Vec3i Coord;
            public MeshRef Sleepers, Rails, Ballast;
            // Місця в секціях, під якими стоїть чужий блок
            public MeshRef Blocked;

            public void Dispose()
            {
                Sleepers?.Dispose();
                Rails?.Dispose();
                Ballast?.Dispose();
                Blocked?.Dispose();
            }
        }

        private ICoreClientAPI capi;
        private RailWaySystem system;
        private WireframeCube cube;
        private MeshRef selectedFill;

        // Свій шейдер заливки: копія ігрового blockhighlights з кольором, на який множиться колір вершин
        // (assets/railworld/shaders/railfill.*). null, якщо не зібрався: тоді заливки не малюються
        private IShaderProgram fillProg;

        private Dictionary<Vec3i, ChunkFill> fills = new Dictionary<Vec3i, ChunkFill>();
        // Чанки, заливку яких треба зібрати заново. Збирається вона, лише коли чанк треба намалювати
        private HashSet<Vec3i> dirtyChunks = new HashSet<Vec3i>();
        // Змінені чанки, які вже видно: їх збирають перед малюванням. Між кадрами список лише очищається
        private readonly List<Vec3i> visibleDirty = new List<Vec3i>();

        private Matrixf mat = new Matrixf();
        private float[] basis = Mat4f.Create();
        private Vec3i chunkCoord = new Vec3i();

        public double RenderOrder => 0.6;
        public int RenderRange => 99;

        public RailSelectionRenderer(ICoreClientAPI capi, RailWaySystem system)
        {
            this.capi = capi;
            this.system = system;
            // Білий каркас, колір задається при малюванні
            cube = WireframeCube.CreateCenterOriginCube(capi, -1);

            MeshData selected = new MeshData(24, 36, false, false, true, false);
            AddBox(selected, SelectedRgba, 0, 0, 0, new Vec3d(1, 0, 0), new Vec3d(0, 1, 0), new Vec3d(0, 0, 1), 1, -1, 1, 1);
            selectedFill = capi.Render.UploadMesh(selected);

            // Чанки, дані яких прийшли раніше, ніж створився рендерер
            foreach (Vec3i coord in system.ClientChunkCoords)
                dirtyChunks.Add(coord.Clone());

            capi.Event.ReloadShader += LoadShader;
            LoadShader();

            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "railselection");
            capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, "railselection-fill");
        }

        // Збирає шейдер заливки. Викликається при старті і щоразу, коли гра перезбирає шейдери
        private bool LoadShader()
        {
            IShaderProgram loaded = capi.Shader.NewShaderProgram();
            loaded.AssetDomain = "railworld";
            loaded.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            loaded.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
            capi.Shader.RegisterFileShaderProgram("railfill", loaded);
            bool ok = loaded.Compile();
            fillProg = ok ? loaded : null;
            if (!ok) capi.Logger.Error("[RailWorld] шейдер railfill не зібрався: порожні місця під деталі не підсвічуватимуться");
            return ok;
        }

        /// <summary>
        /// Бокси чанка змінилися або зникли: його заливку треба зібрати заново. Стара звільняється одразу: чанк,
        /// який вивантажився, не тримає її на відеокарті, а новий збереться, коли чанк знову треба буде намалювати.
        /// </summary>
        public void MarkChunkDirty(Vec3i coord)
        {
            if (fills.TryGetValue(coord, out ChunkFill old))
            {
                old.Dispose();
                fills.Remove(coord);
            }
            dirtyChunks.Add(coord.Clone());
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            IClientPlayer player = capi.World.Player;
            if (player?.Entity == null) return;

            SectionSelection sel = system.CurrentSelection;

            if (stage == EnumRenderStage.OIT)
            {
                RenderFills(player, sel);
                return;
            }

            SectionBox box = sel?.Box;
            if (box == null) return;

            Vec4f color = system.Handler.GetSelectionColor(capi, sel)
                ?? (box.Part == SectionPart.Whole ? WholeSectionColor : box.Installed ? SelectionColor : InstallableOutlineColor);
            // Поки деталь ламають, лінія товщає разом із прогресом
            SetBoxMatrix(box, true);
            cube.Render(capi, mat, 2f + 4f * system.CurrentBreakProgress, color);
        }

        // Порожні місця під деталі: зелені, якщо предмет у руці сюди підходить, інакше білі
        private void RenderFills(IClientPlayer player, SectionSelection sel)
        {
            if (fillProg == null || fillProg.Disposed) return;

            SectionHandler handler = system.Handler;
            ItemStack held = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            bool sleeperFits = handler.CanInstall(held, SectionPart.Sleeper);
            bool railFits = handler.CanInstall(held, SectionPart.FirstRail) || handler.CanInstall(held, SectionPart.SecondRail);
            bool ballastFits = handler.CanInstall(held, SectionPart.Ballast);
            bool showAll = handler.ShowsAllEmptyParts(held);
            if (!sleeperFits && !railFits && !ballastFits && !showAll) return;

            IShaderProgram prog = fillProg;
            prog.Use();
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            prog.UniformMatrix("viewMatrix", capi.Render.CameraMatrixOriginf);
            // Туман кадру, як у колії (RailWayRendererSystem.SetFrameUniforms): заливки ховаються в нього разом із нею
            prog.Uniform("rgbaFogIn", capi.Render.FogColor);
            prog.Uniform("fogMinIn", capi.Render.FogMin);
            prog.Uniform("fogDensityIn", capi.Render.FogDensity);
            capi.Render.GlDisableCullFace();

            // Заливки збираються лише тоді, коли їх треба намалювати: чанк, якого гравець з деталлю в руках так і не
            // побачить, меша заливки не отримує зовсім
            if (showAll)
            {
                // З предметом для прокладання колії видно порожні місця в усіх завантажених чанках, які видно, як і
                // саму колію (ChunkCulling)
                if (dirtyChunks.Count > 0)
                {
                    foreach (Vec3i coord in dirtyChunks)
                    {
                        if (ChunkCulling.Visible(capi, coord.X, coord.Y, coord.Z, false)) visibleDirty.Add(coord);
                    }
                    foreach (Vec3i coord in visibleDirty) BuildFill(coord);
                    visibleDirty.Clear();
                }
                foreach (ChunkFill fill in fills.Values)
                {
                    if (ChunkCulling.Visible(capi, fill.Coord.X, fill.Coord.Y, fill.Coord.Z, false)) RenderChunkFill(prog, fill, sleeperFits, railFits, ballastFits);
                }
            }
            else
            {
                // Зі шпалою чи рейкою в руці: чанк гравця і сусідні
                Vec3d pos = player.Entity.Pos.XYZ;
                int cx = (int)Math.Floor(pos.X / GlobalConstants.ChunkSize);
                int cy = (int)Math.Floor(pos.Y / GlobalConstants.ChunkSize);
                int cz = (int)Math.Floor(pos.Z / GlobalConstants.ChunkSize);

                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    for (int y = cy - 1; y <= cy + 1; y++)
                    {
                        for (int z = cz - 1; z <= cz + 1; z++)
                        {
                            chunkCoord.Set(x, y, z);
                            if (dirtyChunks.Contains(chunkCoord)) BuildFill(chunkCoord);
                            if (fills.TryGetValue(chunkCoord, out ChunkFill fill))
                                RenderChunkFill(prog, fill, sleeperFits, railFits, ballastFits);
                        }
                    }
                }
            }

            // Місце під прицілом яскравіше
            if (sel?.Box != null && !sel.Box.Installed)
            {
                SetBoxMatrix(sel.Box, false);
                prog.UniformMatrix("modelMatrix", mat.Values);
                // Колір цього меша в його вершинах
                prog.Uniform("tint", NoTint);
                capi.Render.RenderMesh(selectedFill);
            }

            capi.Render.GlEnableCullFace();
            prog.Stop();
        }

        private void RenderChunkFill(IShaderProgram prog, ChunkFill fill, bool sleeperFits, bool railFits, bool ballastFits)
        {
            Vec3d cam = capi.World.Player.Entity.CameraPos;
            // Лише розміщення відносно камери: саму камеру шейдер бере з viewMatrix
            mat.Identity()
               .Translate((float)(fill.Coord.X * (double)GlobalConstants.ChunkSize - cam.X), (float)(fill.Coord.Y * (double)GlobalConstants.ChunkSize - cam.Y), (float)(fill.Coord.Z * (double)GlobalConstants.ChunkSize - cam.Z));
            prog.UniformMatrix("modelMatrix", mat.Values);

            DrawFill(prog, fill.Sleepers, sleeperFits ? InstallableTint : GhostTint);
            DrawFill(prog, fill.Rails, railFits ? InstallableTint : GhostTint);
            DrawFill(prog, fill.Ballast, ballastFits ? InstallableTint : GhostTint);
            DrawFill(prog, fill.Blocked, BlockedTint);
        }

        private void DrawFill(IShaderProgram prog, MeshRef mesh, Vec4f tint)
        {
            if (mesh == null) return;
            prog.Uniform("tint", tint);
            capi.Render.RenderMesh(mesh);
        }

        // Збирає заливку зміненого чанка. Стару вже звільнив MarkChunkDirty
        private void BuildFill(Vec3i coord)
        {
            dirtyChunks.Remove(coord);
            List<SectionBox> boxes = system.GetClientBoxes(coord);
            if (boxes == null) return;

            Vec3i at = coord.Clone();
            ChunkFill fill = BuildChunkFill(at, boxes);
            if (fill != null) fills[at] = fill;
        }

        // У якій групі малювати порожнє місце
        private enum FillGroup { Sleeper, Rail, Blocked, Ballast }

        private static FillGroup GroupOf(SectionBox box)
        {
            if (box.Section.Blocked) return FillGroup.Blocked;
            if (box.Part == SectionPart.Ballast) return FillGroup.Ballast;
            return box.Part == SectionPart.Sleeper ? FillGroup.Sleeper : FillGroup.Rail;
        }

        private ChunkFill BuildChunkFill(Vec3i coord, List<SectionBox> boxes)
        {
            int[] counts = new int[4];
            foreach (SectionBox box in boxes)
            {
                if (!box.Installed) counts[(int)GroupOf(box)]++;
            }
            if (counts[0] == 0 && counts[1] == 0 && counts[2] == 0 && counts[3] == 0) return null;

            return new ChunkFill
            {
                Coord = coord,
                Sleepers = BuildFillMesh(coord, boxes, FillGroup.Sleeper, counts[0]),
                Rails = BuildFillMesh(coord, boxes, FillGroup.Rail, counts[1]),
                Ballast = BuildFillMesh(coord, boxes, FillGroup.Ballast, counts[3]),
                Blocked = BuildFillMesh(coord, boxes, FillGroup.Blocked, counts[2])
            };
        }

        // Один меш з усіх порожніх місць чанка однієї групи. Координати вершин відносно початку чанка
        private MeshRef BuildFillMesh(Vec3i coord, List<SectionBox> boxes, FillGroup group, int count)
        {
            if (count == 0) return null;

            MeshData mesh = new MeshData(24 * count, 36 * count, false, false, true, false);
            double ox = coord.X * (double)GlobalConstants.ChunkSize, oy = coord.Y * (double)GlobalConstants.ChunkSize, oz = coord.Z * (double)GlobalConstants.ChunkSize;

            foreach (SectionBox box in boxes)
            {
                if (box.Installed || GroupOf(box) != group) continue;
                AddBox(mesh, WhiteRgba, box.Center.X - ox, box.Center.Y - oy, box.Center.Z - oz,
                    box.AxisSide, box.AxisUp, box.AxisAlong, box.HalfSide, box.MinUp, box.MaxUp, box.HalfAlong);
            }

            return capi.Render.UploadMesh(mesh);
        }

        // Додає в меш один бокс з кольором у вершинах, у тому ж форматі, що й підсвічування блоків у грі
        internal static void AddBox(MeshData mesh, int[] rgba, double cx, double cy, double cz, Vec3d side, Vec3d up, Vec3d along,
            double halfSide, double minUp, double maxUp, double halfAlong)
        {
            for (int f = 0; f < 6; f++)
            {
                float shade = FaceShading[f];
                int color = ColorUtil.ToRgba(rgba[3], (int)(rgba[2] * shade), (int)(rgba[1] * shade), (int)(rgba[0] * shade));
                int first = mesh.VerticesCount;

                for (int v = 0; v < 4; v++)
                {
                    double s = Faces[f][v * 3] * halfSide;
                    double u = Faces[f][v * 3 + 1] > 0 ? maxUp : minUp;
                    double a = Faces[f][v * 3 + 2] * halfAlong;

                    mesh.AddVertexSkipTex(
                        (float)(cx + side.X * s + up.X * u + along.X * a),
                        (float)(cy + side.Y * s + up.Y * u + along.Y * a),
                        (float)(cz + side.Z * s + up.Z * u + along.Z * a),
                        color);
                }

                mesh.AddIndex(first);
                mesh.AddIndex(first + 1);
                mesh.AddIndex(first + 2);
                mesh.AddIndex(first);
                mesh.AddIndex(first + 2);
                mesh.AddIndex(first + 3);
            }
        }

        // Матриця, що ставить куб від -1 до 1 на місце боксу
        // Матриця бокса: розміщення відносно камери, а з withCamera ще й сама камера (для каркаса, який бере
        // матрицю цілком)
        private void SetBoxMatrix(SectionBox box, bool withCamera)
        {
            Vec3d cam = capi.World.Player.Entity.CameraPos;

            // Стовпці матриці: бік, верх, уздовж колії
            basis[0] = (float)box.AxisSide.X;  basis[1] = (float)box.AxisSide.Y;  basis[2] = (float)box.AxisSide.Z;   basis[3] = 0;
            basis[4] = (float)box.AxisUp.X;    basis[5] = (float)box.AxisUp.Y;    basis[6] = (float)box.AxisUp.Z;     basis[7] = 0;
            basis[8] = (float)box.AxisAlong.X; basis[9] = (float)box.AxisAlong.Y; basis[10] = (float)box.AxisAlong.Z; basis[11] = 0;
            basis[12] = 0; basis[13] = 0; basis[14] = 0; basis[15] = 1;

            if (withCamera) mat.Set(capi.Render.CameraMatrixOriginf);
            else mat.Identity();
            mat.Translate((float)(box.Center.X - cam.X), (float)(box.Center.Y - cam.Y), (float)(box.Center.Z - cam.Z))
               .Mul(basis)
               .Translate(0f, (float)((box.MinUp + box.MaxUp) / 2), 0f)
               .Scale((float)box.HalfSide, (float)((box.MaxUp - box.MinUp) / 2), (float)box.HalfAlong);
        }

        public void Dispose()
        {
            capi.Event.ReloadShader -= LoadShader;
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            capi.Event.UnregisterRenderer(this, EnumRenderStage.OIT);
            cube?.Dispose();
            selectedFill?.Dispose();
            foreach (ChunkFill fill in fills.Values)
                fill.Dispose();
            fills.Clear();
        }
    }
}
