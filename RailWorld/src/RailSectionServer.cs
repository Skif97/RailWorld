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
using Vintagestory.GameContent;

namespace RailWorld
{
    [ProtoContract]
    public class RailSectionServer
    {
        private Vec3i _chunkAddres; //адрес и стартовая точка для отрисовки
        private int _numberInChunk;
        private float _trackWidth;

        private Vec3f _startPosition;
        private Vec3f _startTangent; //нормализированный вектор
        private Vec3f _startNormal; //нормализированный вектор

        private Vec3f _centerPosition;
        private Vec3f _centerTangent; //нормализированный вектор
        private Vec3f _centerNormal; //нормализированный вектор

        private double _centerNormalCorrection;

        private Vec3f _endPosition;
        private Vec3f _endTangent; //нормализированный вектор
        private Vec3f _endNormal; //нормализированный вектор


        private Vec3i _nextSectionChunkAddres;
        private int _nextSectionNumberInChunk;

        private Vec3i _previusSectionChunkAddres;
        private int _previusSectionNumberInChunk;

        private bool _firstRailInstalled;
        private bool _secondRailInstalled;
        private bool _sleeperInstalled;

        /// <summary>
        /// Использовать если начальная, центральная и конечная точки находятся в глобальных координатах.
        /// </summary>

        public RailSectionServer(Vec3i chunkAddres, int numberInChunk, float trackWidth, PointOnBezierCurve start, PointOnBezierCurve center, PointOnBezierCurve end) 
        {

            Initialization(chunkAddres, numberInChunk, trackWidth, start.position.Sub(chunkAddres).ToVec3f(), start.tangent, start.normal, center.position.Sub(chunkAddres).ToVec3f(), center.tangent, center.normal, end.position.Sub(chunkAddres).ToVec3f(), end.tangent, end.normal);

        }

        /// <summary>
        /// Использовать если начальная, центральная и конечная точки находятся в координатах чанка.
        /// </summary>


        private void Initialization(Vec3i chunkAddres, int numberInChunk, float trackWidth, Vec3f startPosition, Vec3f startTangent, Vec3f startNormal, Vec3f centerPosition, Vec3f centerTangent, Vec3f centerNormal, Vec3f endPosition, Vec3f endTangent, Vec3f endNormal)
        {
            _chunkAddres = chunkAddres;
            _numberInChunk = numberInChunk;
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

            Vec3f centerToEnd = _endPosition.Clone().Sub(_centerPosition); 
            double centerToEndLength = centerToEnd.Length();
            centerToEnd.Normalize();
            double sin = _centerTangent.Cross(centerToEnd).Length()/_centerTangent.Length()* _centerTangent.Length();
            _centerNormalCorrection = centerToEndLength * sin; 
            //по факту даже на довольно грубых углах это значение корекции очень маленькое около 1/8 части вокселя при чизле
        }

        public Vec3i ChunkAddres
        {
            get { return _chunkAddres; }

        }

        public int NumberInChunk
        {
            get { return _numberInChunk; }

        }

        public float TrackWidth
        {
            get { return _trackWidth; }

        }

        public Vec3f StartPosition
        {
            get { return _startPosition; }

        }

        public Vec3d FullStartPosition
        {
            get { return new Vec3d(_startPosition.X + _chunkAddres.X, _startPosition.Y + _chunkAddres.Y, _startPosition.Z + _chunkAddres.Z); }

        }

        public Vec3d FullEndPosition
        {
            get { return new Vec3d(_endPosition.X + _chunkAddres.X, _endPosition.Y + _chunkAddres.Y, _endPosition.Z + _chunkAddres.Z); }

        }

        public Vec3f StartTangent
        {
            get { return _startTangent; }

        }

        public Vec3f StartNormal
        {
            get { return _startNormal; }

        }

        public Vec3f CenterPosition
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

        public Vec3f EndPosition
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
            get{ return _firstRailInstalled; }
        }

        public bool SecondRailInstalled
        {
            get { return _secondRailInstalled; }
        }

        public bool SleeperInstalled
        {
            get { return _sleeperInstalled; }
        }

        public void UpdateConnections() 
        {
            Vec3i startChunk = ModMath.FindChunk(_startPosition.X + _chunkAddres.X, _startPosition.Y + _chunkAddres.Y, _startPosition.Z + _chunkAddres.Z);
            
            IServerChunk serverChunk = (IServerChunk)RailWorld.coreAPI.World.BlockAccessor.GetChunk(startChunk.X, startChunk.Y, startChunk.Z);
            if (serverChunk.GetModdata<Dictionary<int, RailSectionServer>>("RailSections", null) != null) 
            {
                foreach (var item in RailWorld.coreAPI.World.BlockAccessor.GetChunk(startChunk.X, startChunk.Y, startChunk.Z).GetModdata<Dictionary<int, RailSectionServer>>("RailSections", null))
                {
                    if((_chunkAddres == startChunk) && (_numberInChunk == item.Key)) { continue; }
                    if (FullStartPosition == item.Value.FullStartPosition) 
                    {
                        //проверка двух угловых точек
                    }

                    if (FullStartPosition == item.Value.FullEndPosition)
                    {
                        //проверка двух угловых точек
                    }


                }
            }

            //bool allInOneСhunk = startChunk == endChunk ? true : false;
            //Vec3i endChunk = ModMath.FindChunk(_endPosition.X + _chunkAddres.X, _endPosition.Y + _chunkAddres.Y, _endPosition.Z + _chunkAddres.Z);
            //if (!allInOneСhunk)
            //{
            //    if (RailWorld.coreAPI.World.BlockAccessor.GetChunk(endChunk.X, endChunk.Y, endChunk.Z).GetModdata<Dictionary<int, RailSectionServer>>("RailSections", null) != null)
            //    {
            //        foreach (var item in RailWorld.coreAPI.World.BlockAccessor.GetChunk(endChunk.X, endChunk.Y, endChunk.Z).GetModdata<Dictionary<int, RailSectionServer>>("RailSections", null))
            //        {

            //        }
            //    }
            //}

        }
    }
}
