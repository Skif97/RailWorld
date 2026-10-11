using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace RailWorld.src.RailWay
{
    [ProtoContract]
    public class Section
    {
        [ProtoMember(1)]
        private Vec3i _chunkAddres; //адрес и стартовая точка для отрисовки
        [ProtoMember(2)]
        private float _trackWidth;
       
        [ProtoMember(3)]
        private Vec3d _startPosition;
        [ProtoMember(4)]
        private Vec3f _startTangent; //нормализированный вектор
        [ProtoMember(5)]
        private Vec3f _startNormal; //нормализированный вектор
        
        [ProtoMember(6)]
        private Vec3d _centerPosition;
        [ProtoMember(7)]
        private Vec3f _centerTangent; //нормализированный вектор
        [ProtoMember(8)]
        private Vec3f _centerNormal; //нормализированный вектор

        [ProtoMember(9)]
        private double _centerNormalCorrection;

        [ProtoMember(10)]
        private Vec3d _endPosition;
        [ProtoMember(11)]
        private Vec3f _endTangent; //нормализированный вектор
        [ProtoMember(12)]
        private Vec3f _endNormal; //нормализированный вектор

        [ProtoMember(17)]
        private bool _firstRailInstalled;
        [ProtoMember(18)]
        private bool _secondRailInstalled;
        [ProtoMember(19)]
        private bool _sleeperInstalled;

        [ProtoMember(23)]
        private string _sleeperMaterial;
        [ProtoMember(24)]
        private string _railMaterial; // матеріал першої рейки
        [ProtoMember(25)]
        private string _secondRailMaterial;
        [ProtoMember(26)]
        private string _sleeperType;
        [ProtoMember(27)]
        private string _firstRailType;
        [ProtoMember(28)]
        private string _secondRailType;

        // Сусіди на кожному кінці. Зазвичай по одному; кілька буде на стрілках
        [ProtoMember(29)]
        private List<SectionLink> _startLinks;
        [ProtoMember(30)]
        private List<SectionLink> _endLinks;
        // Який зі зв'язків кінця зараз активний, якщо їх кілька
        [ProtoMember(31)]
        private int _activeStartLink;
        [ProtoMember(32)]
        private int _activeEndLink;

        // Довжина шпали впоперек колії. Разом із шириною колії задає тип колії: звичайна, вузька шахтна тощо
        [ProtoMember(36)]
        private float _sleeperLength;

        /// <summary>
        /// Довжина шпали впоперек колії. Вона ж ширина смуги, яку колія займає на землі.
        /// </summary>
        public float SleeperLength
        {
            // У секцій, збережених до появи цього поля, шпала була завдовжки з блок
            get { return _sleeperLength > 0 ? _sleeperLength : TrackGauge.LegacySleeperLength; }
        }

        // Підсипка: гравій під колією. Матеріал це порода гравію
        [ProtoMember(34)]
        private bool _ballastInstalled;
        [ProtoMember(35)]
        private string _ballastMaterial;

        public bool BallastInstalled
        {
            get { return _ballastInstalled; }
        }

        public string BallastMaterial
        {
            get { return _ballastMaterial; }
        }

        // Під секцією стоїть чужий блок: деталі ставити не можна, доки його не приберуть
        [ProtoMember(33)]
        private bool _blocked;

        public bool Blocked
        {
            get { return _blocked; }
            set { _blocked = value; }
        }

        private static readonly List<SectionLink> NoLinks = new List<SectionLink>();

        /// <summary>
        /// Номер секції в її чанку. Не зберігається, проставляється з ключа словника DataInChunk.
        /// </summary>
        public int IndexInChunk { get; internal set; } = -1;

        public Section() { }// для серриализатора

        // У секцій, збережених до появи окремого матеріалу другої рейки, обидві рейки були з одного.
        // Закріплюємо його одразу після читання, щоб заміна першої рейки не міняла другу
        [ProtoAfterDeserialization]
        private void OnDeserialized()
        {
            if (_secondRailMaterial == null) _secondRailMaterial = _railMaterial;
        }

        /// <summary>
        /// Использовать если начальная, центральная и конечная точки находятся в глобальных координатах.
        /// </summary>

        public Section(PointOnBezierCurve start, PointOnBezierCurve center, PointOnBezierCurve end, float trackWidth, float sleeperLength)
        {
            _sleeperLength = sleeperLength;
            Vec3i chunkAddres = ModMath.FindChunk(center.position);
            Vec3d start_ = start.position.SubCopy(chunkAddres.X*32, chunkAddres.Y * 32, chunkAddres.Z * 32);
            Vec3d center_ = center.position.SubCopy(chunkAddres.X * 32, chunkAddres.Y * 32, chunkAddres.Z * 32);
            Vec3d end_ = end.position.SubCopy(chunkAddres.X * 32, chunkAddres.Y * 32, chunkAddres.Z * 32);
            Initialization(chunkAddres, trackWidth, start_, start.tangent, start.normal, center_, center.tangent, center.normal, end_, end.tangent, end.normal);

        }

        /// <summary>
        /// Использовать если начальная, центральная и конечная точки находятся в координатах чанка.
        /// </summary>


        private void Initialization(Vec3i chunkAddres, float trackWidth, Vec3d startPosition, Vec3f startTangent, Vec3f startNormal, Vec3d centerPosition, Vec3f centerTangent, Vec3f centerNormal, Vec3d endPosition, Vec3f endTangent, Vec3f endNormal)
        {
            _chunkAddres = chunkAddres;
            _trackWidth = trackWidth;
            _startPosition = startPosition;
            _startTangent = startTangent;
            _startNormal = startNormal;
            _centerPosition = centerPosition;
            _centerTangent = centerTangent;
            _centerNormal = centerNormal;
            _endPosition = endPosition;
            _endTangent = endTangent;
            _endNormal = endNormal;

            Vec3d centerToEnd = _endPosition.Clone().Sub(_centerPosition);
            double centerToEndLength = centerToEnd.Length();
            centerToEnd.Normalize();
            double sin = _centerTangent.Cross(centerToEnd.ToVec3f()).Length() / _centerTangent.Length() * _centerTangent.Length();
            _centerNormalCorrection = centerToEndLength * sin;
            //по факту даже на довольно грубых углах это значение корекции очень маленькое около 1/8 части вокселя при чизле
        }

        public Vec3i ChunkAddres
        {
            get { return _chunkAddres; }

        }

        public float TrackWidth
        {
            get { return _trackWidth; }

        }

        public Vec3d StartPosition
        {
            get { return _startPosition; }

        }

        public Vec3d FullStartPosition
        {
            get { return new Vec3d(_chunkAddres.X * 32.0 + _startPosition.X, _chunkAddres.Y * 32.0 + _startPosition.Y, _chunkAddres.Z * 32.0 + _startPosition.Z); }

        }

        public Vec3d FullEndPosition
        {
            get { return new Vec3d(_chunkAddres.X * 32.0 + _endPosition.X, _chunkAddres.Y * 32.0 + _endPosition.Y, _chunkAddres.Z * 32.0 + _endPosition.Z); }

        }

        public Vec3f StartTangent
        {
            get { return _startTangent; }

        }

        public Vec3f StartNormal
        {
            get { return _startNormal; }

        }

        public Vec3d CenterPosition
        {
            get { return _centerPosition; }

        }

        public Vec3f CenterTangent
        {
            get { return _centerTangent; }

        }

        public Vec3f CenterNormal
        {
            get { return _centerNormal; }

        }

        public Vec3d EndPosition
        {
            get { return _endPosition; }

        }

        public Vec3f EndTangent
        {
            get { return _endTangent; }

        }

        public Vec3f EndNormal
        {
            get { return _endNormal; }

        }//наверное можно убрать

        public bool FullyBuilt
        {
            get
            {
                if (_firstRailInstalled && _secondRailInstalled && _sleeperInstalled)
                {
                    return true;
                }
                return false;
            }


        }

        public bool FirstRailInstalled
        {
            get { return _firstRailInstalled; }
        }

        public bool SecondRailInstalled
        {
            get { return _secondRailInstalled; }
        }

        public bool SleeperInstalled
        {
            get { return _sleeperInstalled; }
        }

        public string SleeperMaterial
        {
            get { return _sleeperMaterial; }
        }

        public string RailMaterial
        {
            get { return _railMaterial; }
        }



        public Vec3d GetGlobalPos()
        {
            return new Vec3d(_chunkAddres.X * 32.0 + _centerPosition.X, _chunkAddres.Y * 32.0 + _centerPosition.Y, _chunkAddres.Z * 32.0 + _centerPosition.Z);
        }

        public bool InstallationRails(string material) 
        {
            if (!_firstRailInstalled)
            {
                _firstRailInstalled = true;
                _secondRailInstalled = true;
                _railMaterial = material;
                _secondRailMaterial = material;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Встановлює одну деталь і запам'ятовує тип і матеріал предмета, з якого її зроблено.
        /// </summary>
        public bool InstallPart(SectionPart part, string type, string material)
        {
            if (IsInstalled(part)) return false;

            switch (part)
            {
                case SectionPart.Sleeper:
                    _sleeperInstalled = true;
                    _sleeperType = type;
                    _sleeperMaterial = material;
                    break;
                case SectionPart.FirstRail:
                    _firstRailInstalled = true;
                    _firstRailType = type;
                    _railMaterial = material;
                    break;
                case SectionPart.SecondRail:
                    _secondRailInstalled = true;
                    _secondRailType = type;
                    _secondRailMaterial = material;
                    break;
                case SectionPart.Ballast:
                    _ballastInstalled = true;
                    _ballastMaterial = material;
                    break;
                default:
                    return false;
            }
            return true;
        }

        public string GetMaterial(SectionPart part)
        {
            switch (part)
            {
                case SectionPart.Sleeper: return _sleeperMaterial;
                case SectionPart.FirstRail: return _railMaterial;
                case SectionPart.Ballast: return _ballastMaterial;
                default: return _secondRailMaterial;
            }
        }

        public string GetPartType(SectionPart part)
        {
            switch (part)
            {
                case SectionPart.Sleeper: return _sleeperType;
                case SectionPart.FirstRail: return _firstRailType;
                default: return _secondRailType;
            }
        }

        public bool IsEmpty
        {
            get { return !_firstRailInstalled && !_secondRailInstalled && !_sleeperInstalled && !_ballastInstalled; }
        }

        public bool IsInstalled(SectionPart part)
        {
            switch (part)
            {
                case SectionPart.Sleeper: return _sleeperInstalled;
                case SectionPart.FirstRail: return _firstRailInstalled;
                case SectionPart.SecondRail: return _secondRailInstalled;
                case SectionPart.Ballast: return _ballastInstalled;
                // Секція цілком існує завжди, навіть без деталей
                default: return true;
            }
        }

        public void RemovePart(SectionPart part)
        {
            switch (part)
            {
                case SectionPart.Sleeper: _sleeperInstalled = false; break;
                case SectionPart.FirstRail: _firstRailInstalled = false; break;
                case SectionPart.SecondRail: _secondRailInstalled = false; break;
                case SectionPart.Ballast: _ballastInstalled = false; break;
            }
        }

        public bool InstallationSleeper(string material)
        {
            if (!_sleeperInstalled)
            {
                _sleeperInstalled = true;
                _sleeperMaterial = material;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Їхати можна там, де стоять обидві рейки.
        /// </summary>
        public bool RailsInstalled
        {
            get { return _firstRailInstalled && _secondRailInstalled; }
        }

        public Vec3d GetEndPosition(bool atStart)
        {
            return atStart ? FullStartPosition : FullEndPosition;
        }

        /// <summary>
        /// Напрямок, у якому колія виходить із секції через цей кінець.
        /// </summary>
        public Vec3f GetOutwardDirection(bool atStart)
        {
            return atStart ? new Vec3f(-_startTangent.X, -_startTangent.Y, -_startTangent.Z) : _endTangent;
        }

        // Коли стрілку на цьому кінці востаннє почали переводити, у мілісекундах роботи сервера. 0, якщо ніколи
        [ProtoMember(37)]
        private long _startSwitchMs;
        [ProtoMember(38)]
        private long _endSwitchMs;

        // З якого боку колії стоїть важіль стрілки на цьому кінці: 1 з боку нормалі, -1 з протилежного,
        // 0 якщо ще не визначено. Визначається один раз, коли з кінця секції починає виходити друга колія
        [ProtoMember(39)]
        private int _startLeverSide;
        [ProtoMember(40)]
        private int _endLeverSide;

        // Бік важеля на цьому кінці вибрав гравець: сервер його більше не перевибирає
        [ProtoMember(43)]
        private bool _startLeverManual;
        [ProtoMember(44)]
        private bool _endLeverManual;

        // До якої стрілки секція належить як частина гілки. Кінець тут це той, яким секція ближча до стрілки:
        // секція може лежати на гілках двох стрілок одразу, по одній з кожного боку. null, якщо не належить
        [ProtoMember(41)]
        private SwitchRef _startSwitchRef;
        [ProtoMember(42)]
        private SwitchRef _endSwitchRef;

        // У вузлі якого перетину колій секція лежить. null, якщо ні в якому
        [ProtoMember(45)]
        private CrossingRef _crossingRef;

        /// <summary>Перетин, у вузлі якого лежить секція, або null.</summary>
        public CrossingRef GetCrossingRef()
        {
            return _crossingRef;
        }

        /// <summary>
        /// Записує, у вузлі якого перетину лежить секція; null прибирає запис. Повертає true, якщо запис змінився.
        /// </summary>
        public bool SetCrossingRef(CrossingRef value)
        {
            if (_crossingRef == null ? value == null : _crossingRef.SameAs(value)) return false;
            _crossingRef = value;
            return true;
        }

        /// <summary>
        /// Стрілка, на гілці якої лежить секція, якщо дивитися з кінця atStart, або null.
        /// </summary>
        public SwitchRef GetSwitchRef(bool atStart)
        {
            return atStart ? _startSwitchRef : _endSwitchRef;
        }

        /// <summary>
        /// Записує, до якої стрілки належить секція з боку кінця atStart; null прибирає запис.
        /// Повертає true, якщо запис змінився.
        /// </summary>
        public bool SetSwitchRef(bool atStart, SwitchRef value)
        {
            SwitchRef current = atStart ? _startSwitchRef : _endSwitchRef;
            if (current == null ? value == null : current.SameAs(value)) return false;

            if (atStart) _startSwitchRef = value;
            else _endSwitchRef = value;
            return true;
        }

        /// <summary>
        /// Скидає стан стрілки на цьому кінці: стрілки тут більше немає. Без цього стара позначка «увімкнена
        /// друга гілка» і старий бік важеля дісталися б наступній стрілці, покладеній на цьому ж кінці.
        /// Повертає true, якщо було що скидати.
        /// </summary>
        public bool ResetSwitch(bool atStart)
        {
            bool changed;
            if (atStart)
            {
                changed = _activeStartLink != 0 || _startSwitchMs != 0 || _startLeverSide != 0 || _startLeverManual;
                _activeStartLink = 0;
                _startSwitchMs = 0;
                _startLeverSide = 0;
                _startLeverManual = false;
            }
            else
            {
                changed = _activeEndLink != 0 || _endSwitchMs != 0 || _endLeverSide != 0 || _endLeverManual;
                _activeEndLink = 0;
                _endSwitchMs = 0;
                _endLeverSide = 0;
                _endLeverManual = false;
            }
            return changed;
        }

        /// <summary>
        /// З якого боку колії стоїть важіль стрілки на цьому кінці: 1 з боку нормалі секції, -1 з протилежного.
        /// </summary>
        public int GetLeverSide(bool atStart)
        {
            int side = atStart ? _startLeverSide : _endLeverSide;
            return side < 0 ? -1 : 1;
        }

        /// <summary>Чи бік важеля на цьому кінці вже вибрано.</summary>
        public bool HasLeverSide(bool atStart)
        {
            return (atStart ? _startLeverSide : _endLeverSide) != 0;
        }

        /// <summary>Чи бік важеля на цьому кінці вибрав гравець, а не сервер.</summary>
        public bool IsLeverManual(bool atStart)
        {
            return atStart ? _startLeverManual : _endLeverManual;
        }

        /// <summary>
        /// Ставить важіль з цього боку. manual каже, що так вирішив гравець: тоді сервер, перераховуючи
        /// стрілку, бік більше не міняє.
        /// </summary>
        public void SetLeverSide(bool atStart, int side, bool manual = false)
        {
            if (atStart)
            {
                _startLeverSide = side < 0 ? -1 : 1;
                _startLeverManual = manual;
            }
            else
            {
                _endLeverSide = side < 0 ? -1 : 1;
                _endLeverManual = manual;
            }
        }

        /// <summary>
        /// Скільки триває саме переведення стрілки. Вагонетка, що заїхала на стрілку в цей час, сходить із колії.
        /// </summary>
        public const int SwitchTimeMs = 1000;

        /// <summary>Чи є на цьому кінці стрілка: колія тут розходиться на дві.</summary>
        public bool HasSwitch(bool atStart)
        {
            return GetLinks(atStart).Count >= 2;
        }

        /// <summary>Котра з двох гілок стрілки зараз увімкнена: 0 або 1.</summary>
        public int GetSwitchPosition(bool atStart)
        {
            int count = GetLinks(atStart).Count;
            if (count == 0) return 0;
            return GameMath.Clamp(atStart ? _activeStartLink : _activeEndLink, 0, count - 1);
        }

        /// <summary>
        /// Ставить стрілку в положення, яке прислав сервер. Для клієнта: переводить стрілку лише сервер.
        /// </summary>
        public void SetSwitchPosition(bool atStart, int position)
        {
            if (atStart) _activeStartLink = position;
            else _activeEndLink = position;
        }

        /// <summary>
        /// Переводить стрілку на іншу гілку і запам'ятовує, коли це почалося.
        /// </summary>
        public void ToggleSwitch(bool atStart, long nowMs)
        {
            int count = GetLinks(atStart).Count;
            if (count < 2) return;

            int next = (GetSwitchPosition(atStart) + 1) % count;
            if (atStart)
            {
                _activeStartLink = next;
                _startSwitchMs = nowMs;
            }
            else
            {
                _activeEndLink = next;
                _endSwitchMs = nowMs;
            }
        }

        /// <summary>
        /// Чи стрілку на цьому кінці саме зараз переводять. Час рахується від запуску сервера, тому після
        /// перезапуску збережене значення виявляється «в майбутньому» і вважається давно минулим.
        /// </summary>
        public bool IsSwitching(bool atStart, long nowMs)
        {
            if (!HasSwitch(atStart)) return false;
            long since = atStart ? _startSwitchMs : _endSwitchMs;
            return since > 0 && nowMs >= since && nowMs - since < SwitchTimeMs;
        }

        public List<SectionLink> GetLinks(bool atStart)
        {
            return (atStart ? _startLinks : _endLinks) ?? NoLinks;
        }

        /// <summary>
        /// Сусід, до якого зараз веде цей кінець, або null.
        /// </summary>
        public SectionLink GetActiveLink(bool atStart)
        {
            List<SectionLink> links = GetLinks(atStart);
            if (links.Count == 0) return null;
            int active = atStart ? _activeStartLink : _activeEndLink;
            return links[GameMath.Clamp(active, 0, links.Count - 1)];
        }

        /// <summary>
        /// Додає зв'язок. Повертає false, якщо такий уже є.
        /// </summary>
        public bool AddLink(bool atStart, SectionLink link)
        {
            List<SectionLink> links = atStart ? _startLinks : _endLinks;
            if (links == null)
            {
                links = new List<SectionLink>();
                if (atStart) _startLinks = links; else _endLinks = links;
            }

            foreach (SectionLink existing in links)
            {
                if (existing.Index == link.Index && existing.AtStart == link.AtStart
                    && existing.ChunkX == link.ChunkX && existing.ChunkY == link.ChunkY && existing.ChunkZ == link.ChunkZ) return false;
            }

            links.Add(link);
            return true;
        }

        public bool RemoveLink(bool atStart, SectionLink link)
        {
            List<SectionLink> links = atStart ? _startLinks : _endLinks;
            return links != null && links.Remove(link);
        }

        /// <summary>
        /// Прибирає з цього кінця всі зв'язки, що ведуть до вказаної секції.
        /// </summary>
        public bool RemoveLink(bool atStart, Section target)
        {
            List<SectionLink> links = atStart ? _startLinks : _endLinks;
            return links != null && links.RemoveAll(link => link.PointsTo(target)) > 0;
        }
    }
}
