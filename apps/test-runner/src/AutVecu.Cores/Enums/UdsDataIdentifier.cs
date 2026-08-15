namespace AutVecu.Cores.Enums;

public enum UdsDataIdentifier : ushort
{
    VehicleSpeed = 0x0100,
    EngineRpm = 0x0101,
    CoolantTemperature = 0x0102,
    CoolantTemperatureFaultInjection = 0x0105,
    Vin = 0xF190,
    VehicleManufacturerSparePartNumber = 0xF187,
    VehicleManufacturerEcuSoftwareNumber = 0xF188,
    EcuSerialNumber = 0xF18C
}
