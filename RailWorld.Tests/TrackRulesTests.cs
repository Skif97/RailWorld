using RailWorld.src.RailWay;
using Vintagestory.API.Datastructures;
using Xunit;

namespace RailWorld.Tests
{
    /// <summary>
    /// Правила спільні для всього процесу, тож тест, що їх міняє, не має йти разом з іншими.
    /// </summary>
    [CollectionDefinition(nameof(TrackRulesTests), DisableParallelization = true)]
    public class TrackRulesCollection { }

    /// <summary>
    /// Правила прокладання з налаштувань світу.
    /// </summary>
    [Collection(nameof(TrackRulesTests))]
    public class TrackRulesTests
    {
        [Fact]
        public void WorldWithoutTheSettingsGetsTheDefaultsAndKeepsThem()
        {
            // Світ, створений до появи налаштувань: сервер записує йому значення за замовчуванням
            var config = new TreeAttribute();
            TrackRules.Load(config, true);

            Assert.Equal(TrackRules.DefaultMinRadiusGauges, config.GetDecimal(TrackRules.MinRadiusCode), 6);
            Assert.Equal(TrackRules.DefaultClearance, config.GetDecimal(TrackRules.ClearanceCode), 6);
            Assert.Equal(TrackRules.DefaultMarkerReach, config.GetDecimal(TrackRules.MarkerReachCode), 6);
        }

        [Fact]
        public void ValuesOfTheWorldAreUsed()
        {
            try
            {
                // Так їх пише і екран налаштувань світу (рядком), і команда /worldconfig (числом)
                var config = new TreeAttribute();
                config.SetString(TrackRules.MinRadiusCode, "6");
                config.SetDouble(TrackRules.ClearanceCode, 3.5);
                TrackRules.Load(config, false);

                Assert.Equal(6 * TrackGauge.StandardWidth, TrackRules.MinRadius, 6);
                Assert.Equal(3.5, TrackRules.Clearance, 6);
                Assert.Equal(3.5 / 2, TrackLevel.Limit, 6);
                Assert.Equal(TrackRules.DefaultMarkerReach, TrackRules.MarkerReach, 6);
                // Клієнт у налаштування світу нічого не дописує
                Assert.False(config.HasAttribute(TrackRules.MarkerReachCode));
            }
            finally
            {
                // Правила спільні для всіх тестів: повертаємо значення за замовчуванням
                TrackRules.Load(new TreeAttribute(), false);
            }
        }
    }
}
