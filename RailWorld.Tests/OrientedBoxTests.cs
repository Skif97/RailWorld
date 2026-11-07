using System;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>Повернута коробка кузова проти коробок блоків, вирівняних по осях світу.</summary>
    public class OrientedBoxTests
    {
        // Коробка 2 × 1 × 1 з центром у (0, 0.5, 0), повернута в плані на angle
        private static OrientedBox Body(double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return new OrientedBox(0, 0.5, 0, c, 0, s, 1, -s, 0, c, 0.5, 0, 1, 0, 0.5);
        }

        [Fact]
        public void BlockInFrontIsHitAndBlockBesideIsNot()
        {
            OrientedBox body = Body(0);
            // Блок, у який кузов заходить носом на чверть блока
            Assert.True(body.Overlaps(0.75, 0, -0.5, 1.75, 1, 0.5));
            // Блок поруч, упритул збоку: дотик перетином не вважається
            Assert.False(body.Overlaps(-0.5, 0, 0.5, 0.5, 1, 1.5));
            // Блок над кузовом
            Assert.False(body.Overlaps(-0.5, 1, -0.5, 0.5, 2, 0.5));
        }

        [Fact]
        public void TurnedBodyMissesTheCornerItsAxisAlignedBoundsWouldHit()
        {
            // На 45° кузов уздовж діагоналі. Блок у куті його вирівняної по осях коробки, але не на ньому самому
            OrientedBox body = Body(Math.PI / 4);
            Assert.False(body.Overlaps(0.55, 0, -1.05, 1.05, 1, -0.55));
            // А блок на діагоналі, куди дивиться ніс, зачеплено
            Assert.True(body.Overlaps(0.5, 0, 0.5, 1.5, 1, 1.5));
        }

        [Fact]
        public void BodyOnASlopeClearsTheGroundUnderIt()
        {
            // Кузов на схилі 45° угору вздовж X, низ на 0.25 над полотном. Полотно проходить через центр низу
            double c = Math.Sqrt(0.5);
            // Центр кузова на 0.75 над полотном уздовж його верху (-c, c, 0)
            var body = new OrientedBox(-0.75 * c, 0.75 * c, 0, c, c, 0, 1, 0, 0, 1, 0.5, -c, c, 0, 0.5);
            // Блок під полотном позаду, з верхом на висоті полотна там
            Assert.False(body.Overlaps(-1.5, -2, -0.5, -0.5, -1.0, 0.5));
        }
    }
}
