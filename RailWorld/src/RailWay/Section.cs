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

        public Section(PointOnBezierCurve start, PointOnBezierCurve center, PointOnBezierCurve end, float trackWidth)
        {
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
                default:
                    _secondRailInstalled = true;
                    _secondRailType = type;
                    _secondRailMaterial = material;
                    break;
            }
            return true;
        }

        public string GetMaterial(SectionPart part)
        {
            switch (part)
            {
                case SectionPart.Sleeper: return _sleeperMaterial;
                case SectionPart.FirstRail: return _railMaterial;
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
            get { return !_firstRailInstalled && !_secondRailInstalled && !_sleeperInstalled; }
        }

        public bool IsInstalled(SectionPart part)
        {
            switch (part)
            {
                case SectionPart.Sleeper: return _sleeperInstalled;
                case SectionPart.FirstRail: return _firstRailInstalled;
                case SectionPart.SecondRail: return _secondRailInstalled;
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
