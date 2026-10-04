using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
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
        private const int ChunkSize = 32;

        private static readonly Vec4f SelectionColor = new Vec4f(0f, 0f, 0f, 0.5f);
        private static readonly Vec4f InstallableOutlineColor = new Vec4f(0.3f, 1f, 0.3f, 0.9f);
        private static readonly Vec4f WholeSectionColor = new Vec4f(1f, 0.3f, 0.3f, 0.9f);

        // Кольори заливки: червоний, зелений, синій, непрозорість
        private static readonly int[] GhostRgba = { 255, 255, 255, 60 };
        private static readonly int[] InstallableRgba = { 70, 255, 70, 90 };
        private static readonly int[] SelectedRgba = { 70, 255, 70, 150 };

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

        // Заливка порожніх місць одного чанка: місця під шпали і під рейки, кожне білим і зеленим
        private class ChunkFill
        {
            public Vec3i Coord;
            public MeshRef SleeperGhost, SleeperInstallable, RailGhost, RailInstallable;

            public void Dispose()
            {
                SleeperGhost?.Dispose();
                SleeperInstallable?.Dispose();
                RailGhost?.Dispose();
                RailInstallable?.Dispose();
            }
        }

        private ICoreClientAPI capi;
        private RailWaySystem system;
        private WireframeCube cube;
        private MeshRef selectedFill;

        private Dictionary<Vec3i, ChunkFill> fills = new Dictionary<Vec3i, ChunkFill>();
        private HashSet<Vec3i> dirtyChunks = new HashSet<Vec3i>();

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

            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "railselection");
            capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, "railselection-fill");
        }

        /// <summary>
        /// Бокси чанка змінилися або зникли: його заливку треба зібрати заново.
        /// </summary>
        public void MarkChunkDirty(Vec3i coord)
        {
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
            SetBoxMatrix(box);
            cube.Render(capi, mat, 2f + 4f * system.CurrentBreakProgress, color);
        }

        // Порожні місця під деталі: зелені, якщо предмет у руці сюди підходить, інакше білі
        private void RenderFills(IClientPlayer player, SectionSelection sel)
        {
            RebuildDirtyChunks();

            SectionHandler handler = system.Handler;
            ItemStack held = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            bool sleeperFits = handler.CanInstall(held, SectionPart.Sleeper);
            bool railFits = handler.CanInstall(held, SectionPart.FirstRail) || handler.CanInstall(held, SectionPart.SecondRail);
            bool showAll = handler.ShowsAllEmptyParts(held);
            if (!sleeperFits && !railFits && !showAll) return;

            IShaderProgram prog = capi.Shader.GetProgram((int)EnumShaderProgram.Blockhighlights);
            prog.Use();
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            capi.Render.GlDisableCullFace();

            if (showAll)
            {
                // З предметом для прокладання колії видно порожні місця в усіх завантажених чанках
                foreach (ChunkFill fill in fills.Values)
                    RenderChunkFill(prog, fill, sleeperFits, railFits);
            }
            else
            {
                // Зі шпалою чи рейкою в руці: чанк гравця і сусідні
                Vec3d pos = player.Entity.Pos.XYZ;
                int cx = (int)Math.Floor(pos.X / ChunkSize);
                int cy = (int)Math.Floor(pos.Y / ChunkSize);
                int cz = (int)Math.Floor(pos.Z / ChunkSize);

                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    for (int y = cy - 1; y <= cy + 1; y++)
                    {
                        for (int z = cz - 1; z <= cz + 1; z++)
                        {
                            chunkCoord.Set(x, y, z);
                            if (fills.TryGetValue(chunkCoord, out ChunkFill fill))
                                RenderChunkFill(prog, fill, sleeperFits, railFits);
                        }
                    }
                }
            }

            // Місце під прицілом яскравіше
            if (sel?.Box != null && !sel.Box.Installed)
            {
                SetBoxMatrix(sel.Box);
                prog.UniformMatrix("modelViewMatrix", mat.Values);
                capi.Render.RenderMesh(selectedFill);
            }

            capi.Render.GlEnableCullFace();
            prog.Stop();
        }

        private void RenderChunkFill(IShaderProgram prog, ChunkFill fill, bool sleeperFits, bool railFits)
        {
            Vec3d cam = capi.World.Player.Entity.CameraPos;
            mat.Set(capi.Render.CameraMatrixOriginf)
               .Translate((float)(fill.Coord.X * (double)ChunkSize - cam.X), (float)(fill.Coord.Y * (double)ChunkSize - cam.Y), (float)(fill.Coord.Z * (double)ChunkSize - cam.Z));
            prog.UniformMatrix("modelViewMatrix", mat.Values);

            MeshRef sleepers = sleeperFits ? fill.SleeperInstallable : fill.SleeperGhost;
            MeshRef rails = railFits ? fill.RailInstallable : fill.RailGhost;
            if (sleepers != null) capi.Render.RenderMesh(sleepers);
            if (rails != null) capi.Render.RenderMesh(rails);
        }

        private void RebuildDirtyChunks()
        {
            if (dirtyChunks.Count == 0) return;

            foreach (Vec3i coord in dirtyChunks)
            {
                if (fills.TryGetValue(coord, out ChunkFill old))
                {
                    old.Dispose();
                    fills.Remove(coord);
                }

                List<SectionBox> boxes = system.GetClientBoxes(coord);
                if (boxes == null) continue;

                ChunkFill fill = BuildChunkFill(coord, boxes);
                if (fill != null) fills[coord] = fill;
            }
            dirtyChunks.Clear();
        }

        private ChunkFill BuildChunkFill(Vec3i coord, List<SectionBox> boxes)
        {
            int sleepers = 0, rails = 0;
            foreach (SectionBox box in boxes)
            {
                if (box.Installed) continue;
                if (box.Part == SectionPart.Sleeper) sleepers++; else rails++;
            }
            if (sleepers == 0 && rails == 0) return null;

            return new ChunkFill
            {
                Coord = coord,
                SleeperGhost = BuildFillMesh(coord, boxes, true, sleepers, GhostRgba),
                SleeperInstallable = BuildFillMesh(coord, boxes, true, sleepers, InstallableRgba),
                RailGhost = BuildFillMesh(coord, boxes, false, rails, GhostRgba),
                RailInstallable = BuildFillMesh(coord, boxes, false, rails, InstallableRgba)
            };
        }

        // Один меш з усіх порожніх місць чанка під шпали або під рейки. Координати вершин відносно початку чанка
        private MeshRef BuildFillMesh(Vec3i coord, List<SectionBox> boxes, bool sleepers, int count, int[] rgba)
        {
            if (count == 0) return null;

            MeshData mesh = new MeshData(24 * count, 36 * count, false, false, true, false);
            double ox = coord.X * (double)ChunkSize, oy = coord.Y * (double)ChunkSize, oz = coord.Z * (double)ChunkSize;

            foreach (SectionBox box in boxes)
            {
                if (box.Installed || (box.Part == SectionPart.Sleeper) != sleepers) continue;
                AddBox(mesh, rgba, box.Center.X - ox, box.Center.Y - oy, box.Center.Z - oz,
                    box.AxisSide, box.AxisUp, box.AxisAlong, box.HalfSide, box.MinUp, box.MaxUp, box.HalfAlong);
            }

            return capi.Render.UploadMesh(mesh);
        }

        // Додає в меш один бокс з кольором у вершинах, у тому ж форматі, що й підсвічування блоків у грі
        private static void AddBox(MeshData mesh, int[] rgba, double cx, double cy, double cz, Vec3d side, Vec3d up, Vec3d along,
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
        private void SetBoxMatrix(SectionBox box)
        {
            Vec3d cam = capi.World.Player.Entity.CameraPos;

            // Стовпці матриці: бік, верх, уздовж колії
            basis[0] = (float)box.AxisSide.X;  basis[1] = (float)box.AxisSide.Y;  basis[2] = (float)box.AxisSide.Z;   basis[3] = 0;
            basis[4] = (float)box.AxisUp.X;    basis[5] = (float)box.AxisUp.Y;    basis[6] = (float)box.AxisUp.Z;     basis[7] = 0;
            basis[8] = (float)box.AxisAlong.X; basis[9] = (float)box.AxisAlong.Y; basis[10] = (float)box.AxisAlong.Z; basis[11] = 0;
            basis[12] = 0; basis[13] = 0; basis[14] = 0; basis[15] = 1;

            mat.Set(capi.Render.CameraMatrixOriginf)
               .Translate((float)(box.Center.X - cam.X), (float)(box.Center.Y - cam.Y), (float)(box.Center.Z - cam.Z))
               .Mul(basis)
               .Translate(0f, (float)((box.MinUp + box.MaxUp) / 2), 0f)
               .Scale((float)box.HalfSide, (float)((box.MaxUp - box.MinUp) / 2), (float)box.HalfAlong);
        }

        public void Dispose()
        {
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
