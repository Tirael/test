using UnitsNet;
using UnitsNet.Units;

namespace zzz.Archivers
{
    public static class JEArgFactory
    {
        public static IJEArg Create(TagType tagType, double value)
        {
            return TagTypeHelper.CreateJEArg(tagType, value);
        }
    }

    public interface IJEArg
    {
        string GetArgAsString(IEUSettings euSettings, IEUNumericFormat numericFormat, CultureInfo cultureInfo, Configuration.Configuration configuration, int pipeId);
    }

    [Serializable]
    
    public class JEDoubleUT : IJEArg
    {
        [DataMember]
        private readonly double value;

        public JEDoubleUT(double value)
        {
            this.value = value;
        }

        public string GetArgAsString(IEUSettings euSettings, IEUNumericFormat numericFormat, CultureInfo cultureInfo, Configuration.Configuration configuration, int pipeId)
        {
            return value.ToString(cultureInfo);
        }
    }

    [Serializable]
    
    public class JEIntUT : IJEArg
    {
        [DataMember]
        private readonly int value;

        public JEIntUT(int value)
        {
            this.value = value;
        }

        public string GetArgAsString(IEUSettings euSettings, IEUNumericFormat numericFormat, CultureInfo cultureInfo, Configuration.Configuration configuration, int pipeId)
        {
            return value.ToString(cultureInfo);
        }
    }

    [Serializable]
    
    public class JEBoolUT : IJEArg
    {
        [DataMember]
        private readonly bool value;

        public JEBoolUT(bool value)
        {
            this.value = value;
        }

        public string GetArgAsString(IEUSettings euSettings, IEUNumericFormat numericFormat, CultureInfo cultureInfo, Configuration.Configuration configuration, int pipeId)
        {
            return value.ToString(cultureInfo);
        }
    }

    [Serializable]
    
    public class JEStringUT : IJEArg
    {
        [DataMember]
        private readonly string value;

        public JEStringUT(string value)
        {
            this.value = value;
        }

        public string GetArgAsString(IEUSettings euSettings, IEUNumericFormat numericFormat, CultureInfo cultureInfo, Configuration.Configuration configuration, int pipeId)
        {
            return value;
        }
    }

    [Serializable]
    
    public class JEDatetimeUT : IJEArg
    {
        [DataMember]
        private readonly DateTime value;

        public JEDatetimeUT(DateTime value)
        {
            this.value = value;
        }

        public string GetArgAsString(IEUSettings euSettings, IEUNumericFormat numericFormat, CultureInfo cultureInfo, Configuration.Configuration configuration, int pipeId)
        {
            return value.ToString(cultureInfo);
        }
    }

    [Serializable]
    
    [KnownType("GetKnownTypes")]
    public abstract class JEDoubleUnit<TUnit> : IJEArg
    {
        [DataMember]
        private readonly double _value;
        [DataMember]
        private readonly TagType _tagType;

        protected JEDoubleUnit(double value, TagType tagType)
        {
            _value = value;
            _tagType = tagType;
        }

        // ReSharper disable UnusedMember.Local
        private static IEnumerable<Type> GetKnownTypes()
        // ReSharper restore UnusedMember.Local
        {
            return new[] { typeof(JEDoublePressure), typeof(JEDoubleFlow), typeof(JEDoubleRotationalSpeed), typeof(JEDoubleTemperature),
                           typeof(JEDoubleDensity), typeof(JEDoubleViscosity), typeof(JEDoubleLength), typeof(JEDoubleRatio), typeof(JEDoubleHeight),
                           typeof(JEDoubleSmallConsumption), typeof(JEDoublePower), typeof(JEDoubleDiameter), typeof(JEDoubleVelocity), typeof(JEHeadPressure)};
        }

        public double GetValue()
        {
            return _value;
        }

        public virtual string GetArgAsString(IEUSettings euSettings, IEUNumericFormat numericFormat, CultureInfo cultureInfo, Configuration.Configuration configuration, int pipeId)
        {
            TUnit baseUnit = (TUnit)_tagType.GetBaseUnit();
            TUnit targetUnit = (TUnit)_tagType.GetUnit(euSettings);
            string format = _tagType.GetUnitFormat(numericFormat);
            string abbreviation = GetAbbreviation(targetUnit, cultureInfo);
            var convertedValue = GetConvertedValue(_value, baseUnit, targetUnit);
            var formattedValue = GetFormattedValue(cultureInfo, convertedValue, format);

            return string.Format("{0} {1}", formattedValue, abbreviation);
        }

        protected abstract string GetAbbreviation(TUnit unit, CultureInfo cultureInfo);

        protected abstract double GetConvertedValue(double value, TUnit baseUnit, TUnit targetUnit);

        private string GetFormattedValue(CultureInfo cultureInfo, double convertedValue, string format)
        {
            return convertedValue.ToString(format, cultureInfo);
        }
    }

    [Serializable]
    public class JEDoublePressure : JEDoubleUnit<PressureUnit>
    {
        public JEDoublePressure(double value)
            : base(value, TagType.Pressure)
        {
        }

        protected override string GetAbbreviation(PressureUnit unit, CultureInfo cultureInfo)
        {
            return Pressure.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, PressureUnit baseUnit, PressureUnit targetUnit)
        {
            return Pressure.From(value, baseUnit)
                           .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEDoubleFlow : JEDoubleUnit<FlowUnit>
    {
        public JEDoubleFlow(double value)
            : base(value, TagType.Flowrate)
        {
        }

        protected override string GetAbbreviation(FlowUnit unit, CultureInfo cultureInfo)
        {
            return Flow.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, FlowUnit baseUnit, FlowUnit targetUnit)
        {
            return Flow.From(value, baseUnit)
                       .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEDoubleRotationalSpeed : JEDoubleUnit<RotationalSpeedUnit>
    {
        public JEDoubleRotationalSpeed(double value)
            : base(value, TagType.PumpRevolution)
        {
        }

        protected override string GetAbbreviation(RotationalSpeedUnit unit, CultureInfo cultureInfo)
        {
            return RotationalSpeed.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, RotationalSpeedUnit baseUnit, RotationalSpeedUnit targetUnit)
        {
            return RotationalSpeed.From(value, baseUnit)
                                  .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEDoubleTime : IJEArg
    {
        [DataMember]
        private readonly double value;

        public JEDoubleTime(double value)
        {
            this.value = value;
        }

        public string GetArgAsString(IEUSettings euSettings, IEUNumericFormat numericFormat, CultureInfo cultureInfo, Configuration.Configuration configuration, int pipeId)
        {
            return value.ToString(cultureInfo);
        }
    }

    [Serializable]
    
    public class JEDoubleTemperature : JEDoubleUnit<TemperatureUnit>
    {
        public JEDoubleTemperature(double value)
            : base(value, TagType.Temperature)
        {
        }

        protected override string GetAbbreviation(TemperatureUnit unit, CultureInfo cultureInfo)
        {
            return Temperature.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, TemperatureUnit baseUnit, TemperatureUnit targetUnit)
        {
            return Temperature.From(value, baseUnit)
                              .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEDoubleDensity : JEDoubleUnit<DensityUnit>
    {
        public JEDoubleDensity(double value)
            : base(value, TagType.Density)
        {
        }

        protected override string GetAbbreviation(DensityUnit unit, CultureInfo cultureInfo)
        {
            return Density.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, DensityUnit baseUnit, DensityUnit targetUnit)
        {
            return Density.From(value, baseUnit)
                          .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEDoubleViscosity : JEDoubleUnit<KinematicViscosityUnit>
    {
        public JEDoubleViscosity(double value)
            : base(value, TagType.Viscosity)
        {
        }

        protected override string GetAbbreviation(KinematicViscosityUnit unit, CultureInfo cultureInfo)
        {
            return KinematicViscosity.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, KinematicViscosityUnit baseUnit, KinematicViscosityUnit targetUnit)
        {
            return KinematicViscosity.From(value, baseUnit)
                                     .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEDoubleLength : JEDoubleUnit<LengthUnit>
    {
        public JEDoubleLength(double value)
            : base(value, TagType.Coordinate)
        {
        }

        protected override string GetAbbreviation(LengthUnit unit, CultureInfo cultureInfo)
        {
            return Length.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, LengthUnit baseUnit, LengthUnit targetUnit)
        {
            return Length.From(value, baseUnit)
                         .As(targetUnit);
        }

        public override string GetArgAsString(IEUSettings euSettings, IEUNumericFormat numericFormat, CultureInfo cultureInfo, Configuration.Configuration configuration, int pipeId)
        {
            LengthUnit baseUnit = (LengthUnit)TagType.Coordinate.GetBaseUnit();
            LengthUnit targetUnit = (LengthUnit)TagType.Coordinate.GetUnit(euSettings);
            string format = TagType.Coordinate.GetUnitFormat(numericFormat);
            string abbreviation = GetAbbreviation(targetUnit, cultureInfo);

            double coordinate = GetValue();
            string name = null;
            if (configuration != null)
            {
                var coordinateMapping = new CoordinateMapping(configuration);
                var nameMapping = new NameMapping(configuration);
                coordinate = coordinateMapping.TransformCoordinate(pipeId, coordinate);
                name = nameMapping.CalculateName(pipeId);
            }

            double convertedValue = GetConvertedValue(coordinate, baseUnit, targetUnit);
            var formattedValue = convertedValue.ToString(format);
            return string.Format("{0} {1} {2}", formattedValue, name, abbreviation);
        }
    }

    [Serializable]
    
    public class JEDoubleRatio : JEDoubleUnit<RatioUnit>
    {
        public JEDoubleRatio(double value)
            : base(value, TagType.ValvePosition)
        {
        }

        protected override string GetAbbreviation(RatioUnit unit, CultureInfo cultureInfo)
        {
            return Ratio.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, RatioUnit baseUnit, RatioUnit targetUnit)
        {
            return Ratio.From(value, baseUnit)
                        .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEDoubleHeight : JEDoubleUnit<LengthUnit>
    {
        public JEDoubleHeight(double value)
            : base(value, TagType.TankLevel)
        {
        }

        protected override string GetAbbreviation(LengthUnit unit, CultureInfo cultureInfo)
        {
            return Length.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, LengthUnit baseUnit, LengthUnit targetUnit)
        {
            return Length.From(value, baseUnit)
                         .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEDoubleSmallConsumption : JEDoubleUnit<FlowUnit>
    {
        public JEDoubleSmallConsumption(double value)
            : base(value, TagType.SmallConsumption)
        {
        }

        protected override string GetAbbreviation(FlowUnit unit, CultureInfo cultureInfo)
        {
            return Flow.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, FlowUnit baseUnit, FlowUnit targetUnit)
        {
            return Flow.From(value, baseUnit)
                       .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEDoublePower : JEDoubleUnit<PowerUnit>
    {
        public JEDoublePower(double value)
            : base(value, TagType.PumpPower)
        {
        }

        protected override string GetAbbreviation(PowerUnit unit, CultureInfo cultureInfo)
        {
            return Power.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, PowerUnit baseUnit, PowerUnit targetUnit)
        {
            return Power.From(value, baseUnit)
                        .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEDoubleDiameter : JEDoubleUnit<LengthUnit>
    {
        public JEDoubleDiameter(double value)
            : base(value, TagType.Diameter)
        {
        }

        protected override string GetAbbreviation(LengthUnit unit, CultureInfo cultureInfo)
        {
            return Length.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, LengthUnit baseUnit, LengthUnit targetUnit)
        {
            return Length.From(value, baseUnit)
                         .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEDoubleVelocity : JEDoubleUnit<SpeedUnit>
    {
        public JEDoubleVelocity(double value)
            : base(value, TagType.Velocity)
        {
        }

        protected override string GetAbbreviation(SpeedUnit unit, CultureInfo cultureInfo)
        {
            return Speed.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, SpeedUnit baseUnit, SpeedUnit targetUnit)
        {
            return Speed.From(value, baseUnit)
                        .As(targetUnit);
        }
    }

    [Serializable]
    
    public class JEHeadPressure : JEDoubleUnit<LengthUnit>
    {
        public JEHeadPressure(double value) :
            base(value, TagType.HeadPressure)
        {
        }

        protected override string GetAbbreviation(LengthUnit unit, CultureInfo cultureInfo)
        {
            return Length.GetAbbreviation(unit, cultureInfo);
        }

        protected override double GetConvertedValue(double value, LengthUnit baseUnit, LengthUnit targetUnit)
        {
            return Length.From(value, baseUnit)
                         .As(targetUnit);
        }
    }
}
