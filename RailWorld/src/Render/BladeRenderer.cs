using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using RailWorld.src.RailWay;

namespace RailWorld
{
    /// <summary>
    /// Точка контуру деталі в одному перерізі: X уздовж Side і Y уздовж Up від точки Center, у блоках.
    /// </summary>
    internal class LoftPoint
    {
        public Vec3d Center, Side, Up;
        public double X, Y;
    }

    /// <summary>
    /// Контур деталі в одному перерізі: точки по колу, від лівого нижнього краю через верх до правого нижнього.
    /// У всіх перерізах деталі точок однаково, і точка з тим самим номером це та сама лінія вздовж деталі.
    /// </summary>
    internal class LoftStation
    {
        public Vec3d Along;
        public LoftPoint[] Points;
    }

    /// <summary>
    /// Частина деталі: поверхня, натягнута на контури в перерізах, і її торці. Деталь може складатися
    /// з кількох частин, усі вони потрапляють в один меш.
    /// </summary>
    internal class LoftPart
    {
        public List<LoftStation> Stations = new List<LoftStation>();
        public List<LoftPoint[]> StartCaps = new List<LoftPoint[]>();
        public List<LoftPoint[]> EndCaps = new List<LoftPoint[]>();
    }

    /// <summary>
    /// Збирає меш гостряка стрілки. Матрицею з готової рейки його не зробити: матриця вміє зсунути, повернути
    /// й розтягнути, але не звузити клином і не зігнути. Тому гостряк будується з перерізів: профіль рейки
    /// протягується вздовж лінії рейки своєї колії, і в кожному перерізі задано, де він стоїть, куди
    /// повернутий і якої ширини. Виходить один суцільний меш без стиків і сходинок.
    /// Форма як у справжнього гостряка: зістружений він лише з боку рамної рейки, а робоча грань, по якій
    /// котиться гребінь колеса, лишається на лінії рейки.
    /// </summary>
    internal static class BladeMesh
    {
        // Пів ширини головки рейки в моделі (shapes/item/rail.json: головка 1.2 вокселя)
        public const double HeadHalfWidth = RailProfile.HeadHalfWidth;

        // На скільки відрізків ділиться проміжок між двома сусідніми перерізами. Перерізи стоять через
        // чверть блока; між ними лінія гостряка веде плавна крива, і частіший поділ робить дугу гладкою
        private const int Subdivisions = 3;

        // Скільки блоків уздовж деталі займає один повтор текстури рейки: стільки ж, скільки у звичайної рейки
        // (shapes/item/rail.json, 32 пікселі на 16 вокселів). Текстура лягає за справжньою довжиною, а не
        // порівну на кожен відрізок між перерізами: перерізи стоять нерівно, біля жолобів і торців густо
        private const double TextureLength = 1;

        // Відбиток вхідних даних деталі (Fingerprint): якщо він той самий, що й минулого разу, меш вийде той
        // самий, і збирати його заново не треба
        public static long Mix(long hash, LoftPoint point)
        {
            return Fingerprint.Mix(Fingerprint.Mix(Fingerprint.Mix(Fingerprint.Mix(Fingerprint.Mix(hash, point.Center), point.Side), point.Up), point.X), point.Y);
        }

        public static long Mix(long hash, LoftPart part)
        {
            foreach (LoftStation station in part.Stations)
            {
                hash = Fingerprint.Mix(hash, station.Along);
                foreach (LoftPoint point in station.Points) hash = Mix(hash, point);
            }
            foreach (List<LoftPoint[]> caps in new[] { part.StartCaps, part.EndCaps })
            {
                hash = Fingerprint.Mix(hash, caps.Count);
                foreach (LoftPoint[] cap in caps) foreach (LoftPoint point in cap) hash = Mix(hash, point);
            }
            return hash;
        }

        /// <summary>
        /// Осі, у яких зберігається меш: напрямок першого перерізу, бік поперек нього і верх поперек обох.
        /// Меш збирається в цих осях, а рендер потім ними ж його повертає, і це дає правильний результат,
        /// лише коли всі три взаємно перпендикулярні. Сам переріз таким бути не мусить: у хрестовини бік
        /// перерізу дивиться від однієї рейки до іншої, навскоси до колії. Тому осі вирівнюються тут.
        /// </summary>
        public static RailFrame Basis(RailFrame first)
        {
            Vec3d along = first.Along.Clone().Normalize();

            Vec3d side = first.Side.Clone();
            side.Sub(along.Clone().Mul(side.Dot(along)));
            if (side.Length() < 1e-6) side = new Vec3d(-along.Z, 0, along.X);
            side.Normalize();

            Vec3d up = side.Cross(along);
            if (up.Dot(first.Up) < 0) up.Mul(-1);

            return new RailFrame { Center = first.Center, Side = side, Up = up, Along = along };
        }

        /// <summary>
        /// Меш деталі, заданої контурами в перерізах: поверхня між сусідніми контурами плюс торці. Вершини
        /// ставляться вручну, тому в меші є лише зовнішня поверхня. Грань між двома точками контуру, які
        /// в обох перерізах збігаються, має нульову площу і в меш не потрапляє.
        /// Між перерізами лінію кожної точки веде та сама плавна крива, що й у гостряка.
        /// Текстура береться з меша рейки template: уздовж деталі вона тягнеться так само, як на гостряку,
        /// упоперек розгортається по контуру, 16 пікселів на блок.
        /// basis це осі, в яких зібрано меш: їх треба передати рендеру.
        /// </summary>
        public static MeshData BuildLoft(MeshData template, List<LoftPart> parts, out RailFrame basis)
        {
            basis = null;
            LoftPart first = parts?.Find(part => part != null && part.Stations.Count >= 2);
            if (first == null) return null;

            LoftStation firstStation = first.Stations[0];
            LoftPoint origin = firstStation.Points[firstStation.Points.Length / 2];
            basis = Basis(new RailFrame { Center = origin.Center, Side = origin.Side, Up = origin.Up, Along = firstStation.Along });
            return BuildLoft(template, parts, basis);
        }

        /// <summary>
        /// Те саме, але в заданих осях basis: так обидва крайні положення рухомої деталі збираються в одних
        /// осях, і та сама вершина в них відрізняється лише зсувом.
        /// </summary>
        public static MeshData BuildLoft(MeshData template, List<LoftPart> parts, RailFrame basis)
        {
            if (template == null || template.VerticesCount == 0 || parts == null || basis == null) return null;
            parts = parts.FindAll(part => part != null && part.Stations.Count >= 2);
            if (parts.Count == 0) return null;

            // Де в атласі лежить текстура рейки: її межі це межі координат текстури в меші рейки
            float u0 = float.MaxValue, v0 = float.MaxValue, u1 = float.MinValue, v1 = float.MinValue;
            for (int v = 0; v < template.VerticesCount; v++)
            {
                u0 = Math.Min(u0, template.Uv[v * 2]); u1 = Math.Max(u1, template.Uv[v * 2]);
                v0 = Math.Min(v0, template.Uv[v * 2 + 1]); v1 = Math.Max(v1, template.Uv[v * 2 + 1]);
            }
            // Трохи всередину, щоб на краю не підтягувалися пікселі сусідньої текстури атласу
            float du = (u1 - u0) * 0.01f, dv = (v1 - v0) * 0.01f;
            u0 += du; u1 -= du; v0 += dv; v1 -= dv;

            // Той самий набір даних на вершину, що й у меша рейки
            MeshData mesh = template.Clone();
            mesh.Clear();

            foreach (LoftPart part in parts)
            {
                List<LoftStation> stations = part.Stations;
                int count = stations[0].Points.Length;
                // Скільки блоків деталі вже вкрито поточним повтором текстури
                double covered = 0;

                for (int i = 0; i + 1 < stations.Count; i++)
                {
                    LoftStation a = stations[i], b = stations[i + 1];
                    if (a.Points.Length != count || b.Points.Length != count) continue;
                    double[] arcA = Arc(a), arcB = Arc(b);

                    for (int m = 0; m < Subdivisions; m++)
                    {
                        double from = (double)m / Subdivisions, to = (double)(m + 1) / Subdivisions;

                        // Довжина відрізка по середній лінії контуру. Текстура в атласі не повторюється сама,
                        // тож відрізок, що не вміщається в залишок повтору, починає новий: там шов
                        double length = Math.Min(Between(a, b, count / 2, from).DistanceTo(Between(a, b, count / 2, to)), TextureLength);
                        if (covered + length > TextureLength) covered = 0;
                        float uFrom = u0 + (u1 - u0) * (float)(covered / TextureLength);
                        float uTo = u0 + (u1 - u0) * (float)((covered + length) / TextureLength);
                        covered += length;

                        for (int k = 0; k < count; k++)
                        {
                            int n = (k + 1) % count;
                            Vec3d p00 = Between(a, b, k, from), p01 = Between(a, b, n, from);
                            Vec3d p10 = Between(a, b, k, to), p11 = Between(a, b, n, to);

                            // Назовні від контуру: упоперек його відрізка
                            double middle = (from + to) / 2;
                            LoftPoint ak = a.Points[k], an = a.Points[n], bk = b.Points[k], bn = b.Points[n];
                            double dx = (an.X - ak.X) * (1 - middle) + (bn.X - bk.X) * middle;
                            double dy = (an.Y - ak.Y) * (1 - middle) + (bn.Y - bk.Y) * middle;
                            Vec3d outward = new Vec3d(
                                -dy * (ak.Side.X + bk.Side.X) + dx * (ak.Up.X + bk.Up.X),
                                -dy * (ak.Side.Y + bk.Side.Y) + dx * (ak.Up.Y + bk.Up.Y),
                                -dy * (ak.Side.Z + bk.Side.Z) + dx * (ak.Up.Z + bk.Up.Z));

                            float vk0 = Across(arcA[k], arcB[k], from, v0, v1), vn0 = Across(arcA[k + 1], arcB[k + 1], from, v0, v1);
                            float vk1 = Across(arcA[k], arcB[k], to, v0, v1), vn1 = Across(arcA[k + 1], arcB[k + 1], to, v0, v1);

                            AddQuad(mesh, basis, outward, p00, p01, p11, p10, uFrom, vk0, uFrom, vn0, uTo, vn1, uTo, vk1);
                        }
                    }
                }

                AddCaps(mesh, basis, part.StartCaps, stations[0].Along.Clone().Mul(-1), u0, u1, v0, v1);
                AddCaps(mesh, basis, part.EndCaps, stations[stations.Count - 1].Along, u0, u1, v0, v1);
            }

            return mesh.VerticesCount == 0 ? null : mesh;
        }

        // Довжина контуру від його початку до кожної точки, блоків. Останнє число це довжина всього контуру
        // разом з низом, який його замикає
        private static double[] Arc(LoftStation station)
        {
            int count = station.Points.Length;
            double[] arc = new double[count + 1];
            for (int k = 1; k <= count; k++)
            {
                LoftPoint p = station.Points[k - 1], q = station.Points[k % count];
                arc[k] = arc[k - 1] + Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
            }
            return arc;
        }

        // Координата текстури впоперек деталі: 16 пікселів на блок контуру, текстура 32 пікселі
        private static float Across(double arcA, double arcB, double t, float v0, float v1)
        {
            double arc = arcA + (arcB - arcA) * t;
            return v0 + (v1 - v0) * (float)GameMath.Clamp(arc / 2, 0, 1);
        }

        private static Vec3d World(LoftPoint p)
        {
            return new Vec3d(p.Center.X + p.Side.X * p.X + p.Up.X * p.Y, p.Center.Y + p.Side.Y * p.X + p.Up.Y * p.Y, p.Center.Z + p.Side.Z * p.X + p.Up.Z * p.Y);
        }

        // Точка контуру з номером k між двома перерізами
        private static Vec3d Between(LoftStation a, LoftStation b, int k, double t)
        {
            LoftPoint p = a.Points[k], q = b.Points[k];
            if (t <= 0) return World(p);
            if (t >= 1) return World(q);

            double span = p.Center.DistanceTo(q.Center);
            double t2 = t * t, t3 = t2 * t;
            double h00 = 2 * t3 - 3 * t2 + 1, h10 = t3 - 2 * t2 + t, h01 = -2 * t3 + 3 * t2, h11 = t3 - t2;
            double cx = h00 * p.Center.X + h10 * span * a.Along.X + h01 * q.Center.X + h11 * span * b.Along.X;
            double cy = h00 * p.Center.Y + h10 * span * a.Along.Y + h01 * q.Center.Y + h11 * span * b.Along.Y;
            double cz = h00 * p.Center.Z + h10 * span * a.Along.Z + h01 * q.Center.Z + h11 * span * b.Along.Z;

            double x = p.X + (q.X - p.X) * t, y = p.Y + (q.Y - p.Y) * t;
            return new Vec3d(
                cx + (p.Side.X + (q.Side.X - p.Side.X) * t) * x + (p.Up.X + (q.Up.X - p.Up.X) * t) * y,
                cy + (p.Side.Y + (q.Side.Y - p.Side.Y) * t) * x + (p.Up.Y + (q.Up.Y - p.Up.Y) * t) * y,
                cz + (p.Side.Z + (q.Side.Z - p.Side.Z) * t) * x + (p.Up.Z + (q.Up.Z - p.Up.Z) * t) * y);
        }

        private static void AddCaps(MeshData mesh, RailFrame basis, List<LoftPoint[]> caps, Vec3d outward, float u0, float u1, float v0, float v1)
        {
            if (caps == null) return;
            foreach (LoftPoint[] cap in caps)
            {
                if (cap == null || cap.Length != 4) continue;
                // Торець малий: йому вистачає клаптика текстури, розміром як він сам
                float width = (float)GameMath.Clamp(Math.Abs(cap[1].X - cap[0].X) / 2, 0.02, 1);
                float height = (float)GameMath.Clamp(Math.Abs(cap[2].Y - cap[1].Y) / 2, 0.02, 1);
                float ua = u0, ub = u0 + (u1 - u0) * width, va = v0, vb = v0 + (v1 - v0) * height;
                AddQuad(mesh, basis, outward, World(cap[0]), World(cap[1]), World(cap[2]), World(cap[3]), ua, va, ub, va, ub, vb, ua, vb);
            }
        }

        // Чотирикутна грань з вершинами по колу. Грань нульової площі пропускається. Нормаль дивиться в бік outward
        private static void AddQuad(MeshData mesh, RailFrame basis, Vec3d outward, Vec3d a, Vec3d b, Vec3d c, Vec3d d,
            float ua, float va, float ub, float vb, float uc, float vc, float ud, float vd)
        {
            Vec3d ab = b.SubCopy(a), ac = c.SubCopy(a), ad = d.SubCopy(a);
            Vec3d first = ab.Cross(ac), second = ac.Cross(ad);
            if (first.Length() + second.Length() < 1e-9) return;

            Vec3d normal = new Vec3d(first.X + second.X, first.Y + second.Y, first.Z + second.Z);
            if (normal.Length() < 1e-12) return;
            normal.Normalize();
            bool flip = normal.Dot(outward) < 0;
            if (flip) normal.Mul(-1);

            int flags = VertexFlags.PackNormal(normal.Dot(basis.Side), normal.Dot(basis.Up), normal.Dot(basis.Along));
            int start = mesh.VerticesCount;
            AddVertex(mesh, basis, a, ua, va, flags);
            AddVertex(mesh, basis, b, ub, vb, flags);
            AddVertex(mesh, basis, c, uc, vc, flags);
            AddVertex(mesh, basis, d, ud, vd, flags);

            if (flip) mesh.AddIndices(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            else mesh.AddIndices(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        private static void AddVertex(MeshData mesh, RailFrame basis, Vec3d p, float u, float v, int flags)
        {
            double x = p.X - basis.Center.X, y = p.Y - basis.Center.Y, z = p.Z - basis.Center.Z;
            mesh.AddVertexWithFlags(
                (float)(x * basis.Side.X + y * basis.Side.Y + z * basis.Side.Z),
                (float)(x * basis.Up.X + y * basis.Up.Y + z * basis.Up.Z),
                (float)(x * basis.Along.X + y * basis.Along.Y + z * basis.Along.Z),
                u, v, ColorUtil.WhiteArgb, flags);
        }
    }

    /// <summary>
    /// Яка це деталь: секція, якій вона належить (у перетину це перетнута секція основної колії), з якого
    /// боку колії і котра з деталей цього боку (tag).
    /// </summary>
    internal readonly struct BladeKey : IEquatable<BladeKey>
    {
        public readonly SectionKey Section;
        public readonly bool Left;
        public readonly string Tag;

        public BladeKey(SectionKey section, bool left, string tag)
        {
            Section = section;
            Left = left;
            Tag = tag ?? "";
        }

        public bool Equals(BladeKey other)
        {
            return Left == other.Left && Section.Equals(other.Section) && string.Equals(Tag, other.Tag, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is BladeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Section.GetHashCode() * 397 ^ StringComparer.Ordinal.GetHashCode(Tag)) * 2 + (Left ? 1 : 0);
            }
        }
    }

    /// <summary>
    /// Малює деталі стрілок: гостряки, хрестовини, контррейки. Кожна деталь це власний меш, один інстанс,
    /// тим самим шейдером і за тим самим принципом, що й рейки та шпали: матриця деталі задана відносно кута
    /// чанка її секції і лежить у відеокарті незмінною, а щокадру шейдерові передається лише кут чанка
    /// відносно камери.
    /// Меш зібрано в осях першого перерізу деталі, і матриця його повертає. В один меш на чанк деталі
    /// не зливаються навмисно: нормаль у вершині гра зберігає трьома бітами на вісь, і в осях деталі грані
    /// рейки лягають на ці осі точно, а в осях світу нормалі повернутої колії округлилися б на кілька
    /// градусів, і деталь стрілки відтінком відрізнялася б від рейки поруч.
    /// </summary>
    internal class BladeRenderer : IDisposable
    {
        private class Blade
        {
            // Меш лежить лише у відеопам'яті. Коли міняється світло, туди дописуються тільки дані інстанса:
            // для цього є окремий порожній меш, у якому крім них нічого немає
            public MeshData Update;
            // Відбиток даних, з яких зібрано меш, і чи підтверджено деталь під час поточної перебудови чанка
            public long Hash;
            public bool Live = true;
            public MeshRef MeshRef;
            public CustomMeshDataPartFloat Floats;
            public int TextureId;
            // Чанк секції, якій належить деталь, і де стоїть початок її меша у світі
            public Vec3i Chunk;
            public Vec3d Origin;
            // Світло одним числом: за ним видно, чи воно змінилося
            public int LightKey;
            // Рухома деталь (гостряк): координати вершин у двох крайніх положеннях і меш, яким у відеокарту
            // дописуються лише координати між ними. null у нерухомої
            public float[] From, To;
            public MeshData Moved;
            public double Open = double.NaN;
        }

        private ICoreClientAPI capi;
        private Dictionary<BladeKey, Blade> blades = new Dictionary<BladeKey, Blade>();
        private Vec3f origin = new Vec3f();

        public BladeRenderer(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        /// <summary>
        /// Ставить деталь секції з чанка chunkCoord: за ним деталь і прибирається. mesh у координатах першого
        /// перерізу first, як його збирає BladeMesh.
        /// </summary>
        public void Set(Vec3i chunkCoord, BladeKey key, long hash, MeshData mesh, RailFrame first, int textureId)
        {
            Remove(key);
            if (mesh == null) return;

            // Світло і матриця одного інстанса: 4 + 16 чисел, так само як у рейок
            var floats = new CustomMeshDataPartFloat(20)
            {
                Instanced = true,
                InterleaveOffsets = new int[] { 0, 16, 32, 48, 64 },
                InterleaveSizes = new int[] { 4, 4, 4, 4, 4 },
                InterleaveStride = 80,
                StaticDraw = false
            };
            // Масив на 20 чисел уже створив конструктор
            float[] values = floats.Values;
            floats.SetAllocationSize(20);
            floats.Count = 20;

            Blade blade = new Blade
            {
                Update = new MeshData(false) { CustomFloats = floats },
                Hash = hash,
                Floats = floats,
                TextureId = textureId,
                Chunk = chunkCoord.Clone(),
                Origin = first.Center.Clone()
            };

            // Стовпці матриці це осі першого перерізу: бік, верх, напрямок. Останній стовпець це початок меша
            // відносно кута чанка
            values[4] = (float)first.Side.X; values[5] = (float)first.Side.Y; values[6] = (float)first.Side.Z; values[7] = 0;
            values[8] = (float)first.Up.X; values[9] = (float)first.Up.Y; values[10] = (float)first.Up.Z; values[11] = 0;
            values[12] = (float)first.Along.X; values[13] = (float)first.Along.Y; values[14] = (float)first.Along.Z; values[15] = 0;
            values[16] = (float)(blade.Origin.X - chunkCoord.X * GlobalConstants.ChunkSize);
            values[17] = (float)(blade.Origin.Y - chunkCoord.Y * GlobalConstants.ChunkSize);
            values[18] = (float)(blade.Origin.Z - chunkCoord.Z * GlobalConstants.ChunkSize);
            values[19] = 1;
            WriteLight(blade, TrackLight.KeyOf(capi.World.BlockAccessor, blade.Origin), TrackLight.Of(capi.World.BlockAccessor, blade.Origin));

            mesh.CustomFloats = floats;
            blade.MeshRef = capi.Render.UploadMesh(mesh);
            blades[key] = blade;
        }

        /// <summary>
        /// Ставить рухому деталь: closed і thrown це її меш у двох крайніх положеннях, зібраний в тих самих осях
        /// first (BladeMesh.BuildLoft), і open, де вона зараз між ними, від 0 до 1. Далі її переставляє Move.
        /// false, якщо вершини двох положень не відповідають одна одній: тоді нічого не ставиться.
        /// </summary>
        public bool SetMoving(Vec3i chunkCoord, BladeKey key, long hash, MeshData closed, MeshData thrown, RailFrame first, int textureId, double open)
        {
            if (closed == null || thrown == null || thrown.VerticesCount != closed.VerticesCount) return false;

            int count = closed.XyzCount;
            var from = new float[count];
            var to = new float[count];
            Array.Copy(closed.xyz, from, count);
            Array.Copy(thrown.xyz, to, count);

            // Координати в цьому меші переписуються під час руху
            closed.XyzStatic = false;
            Set(chunkCoord, key, hash, closed, first, textureId);
            if (!blades.TryGetValue(key, out Blade blade)) return false;

            blade.From = from;
            blade.To = to;
            blade.Moved = new MeshData(false) { xyz = new float[count], VerticesCount = closed.VerticesCount };
            Move(key, open);
            return true;
        }

        /// <summary>
        /// Переставляє рухому деталь у положення open між двома крайніми: кожна вершина на тій самій частці шляху
        /// між ними. Нових мешів не створює, дописує координати в той самий. false, якщо такої рухомої деталі немає.
        /// </summary>
        public bool Move(BladeKey key, double open)
        {
            if (!blades.TryGetValue(key, out Blade blade) || blade.From == null || blade.MeshRef == null) return false;
            if (open == blade.Open) return true;
            blade.Open = open;

            float t = (float)open;
            float[] from = blade.From, to = blade.To, xyz = blade.Moved.xyz;
            for (int i = 0; i < xyz.Length; i++) xyz[i] = from[i] + (to[i] - from[i]) * t;
            capi.Render.UpdateMesh(blade.MeshRef, blade.Moved);
            return true;
        }

        // Кладе світло блока, де стоїть деталь, у дані інстанса: rgb це світло від джерел, a сонячне
        private static void WriteLight(Blade blade, int key, Vec4f light)
        {
            blade.LightKey = key;

            float[] values = blade.Floats.Values;
            values[0] = light.R;
            values[1] = light.G;
            values[2] = light.B;
            values[3] = light.A;
        }

        /// <summary>
        /// Чи лежить під цим ключем деталь, зібрана з тих самих даних. Якщо так, вона лишається як є:
        /// ні збирати меш, ні вивантажувати його заново не треба.
        /// </summary>
        public bool Keep(BladeKey key, long hash)
        {
            if (!blades.TryGetValue(key, out Blade blade) || blade.Hash != hash) return false;
            blade.Live = true;
            return true;
        }

        /// <summary>
        /// Початок перебудови чанка: усі його деталі чекають підтвердження. Ті, що їх перебудова поставить
        /// знову або залишить через Keep, живуть далі; решту прибере EndChunk.
        /// </summary>
        public void BeginChunk(Vec3i chunkCoord)
        {
            foreach (Blade blade in blades.Values)
            {
                if (blade.Chunk.Equals(chunkCoord)) blade.Live = false;
            }
        }

        /// <summary>
        /// Кінець перебудови чанка: прибирає деталі, яких у ньому більше немає.
        /// </summary>
        public void EndChunk(Vec3i chunkCoord)
        {
            foreach (BladeKey key in blades.Where(pair => !pair.Value.Live && pair.Value.Chunk.Equals(chunkCoord)).Select(pair => pair.Key).ToList()) Remove(key);
        }

        private void Remove(BladeKey key)
        {
            if (!blades.TryGetValue(key, out Blade blade)) return;
            blade.MeshRef?.Dispose();
            blades.Remove(key);
        }

        public void RemoveChunk(Vec3i chunkCoord)
        {
            foreach (BladeKey key in blades.Where(pair => pair.Value.Chunk.Equals(chunkCoord)).Select(pair => pair.Key).ToList()) Remove(key);
        }

        /// <summary>
        /// Перевіряє, чи змінилося світло деталей: гра не повідомляє моди про зміну освітлення. Деталі,
        /// чиє світло змінилося, у відеокарті оновлюються лише дані її інстанса.
        /// </summary>
        public void UpdateLights(LightProbe probe)
        {
            foreach (Blade blade in blades.Values)
            {
                int key = probe.KeyOf(blade.Origin);
                if (key == blade.LightKey || blade.MeshRef == null) continue;

                WriteLight(blade, key, probe.Of(blade.Origin));
                capi.Render.UpdateMesh(blade.MeshRef, blade.Update);
            }
        }

        /// <summary>
        /// Малює деталі поточним шейдером. Перед кожною передає шейдерові origin: кут її чанка відносно камери.
        /// bindTexture ставить текстуру деталі; у проході тіней вона не потрібна.
        /// </summary>
        public void Draw(IShaderProgram prog, Vec3d cameraPos, Action<int> bindTexture)
        {
            foreach (Blade blade in blades.Values)
            {
                if (blade.MeshRef == null) continue;

                origin.Set((float)(blade.Chunk.X * GlobalConstants.ChunkSize - cameraPos.X), (float)(blade.Chunk.Y * GlobalConstants.ChunkSize - cameraPos.Y), (float)(blade.Chunk.Z * GlobalConstants.ChunkSize - cameraPos.Z));
                prog.Uniform("origin", origin);
                bindTexture?.Invoke(blade.TextureId);
                capi.Render.RenderMeshInstanced(blade.MeshRef, 1);
            }
        }

        public void Dispose()
        {
            foreach (Blade blade in blades.Values) blade.MeshRef?.Dispose();
            blades.Clear();
        }
    }
}
