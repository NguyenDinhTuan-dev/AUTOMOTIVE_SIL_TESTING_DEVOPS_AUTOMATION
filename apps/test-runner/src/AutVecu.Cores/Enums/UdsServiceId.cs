namespace AutVecu.Cores.Enums;

public enum UdsServiceId : byte
{
    DiagnosticSessionControl = 0x10,
    EcuReset = 0x11,
    ClearDiagnosticInformation = 0x14,
    ReadDtcInformation = 0x19,
    ReadDataByIdentifier = 0x22,
    ReadMemoryByAddress = 0x23,
    ReadScalingDataByIdentifier = 0x24,
    SecurityAccess = 0x27,
    CommunicationControl = 0x28,
    Authentication = 0x29,
    ReadDataByPeriodicIdentifier = 0x2A,
    DynamicallyDefineDataIdentifier = 0x2C,
    WriteDataByIdentifier = 0x2E,
    InputOutputControlByIdentifier = 0x2F,
    RoutineControl = 0x31,
    RequestDownload = 0x34,
    RequestUpload = 0x35,
    TransferData = 0x36,
    RequestTransferExit = 0x37,
    RequestFileTransfer = 0x38,
    WriteMemoryByAddress = 0x3D,
    TesterPresent = 0x3E,
    AccessTimingParameter = 0x83,
    SecuredDataTransmission = 0x84,
    ControlDtcSetting = 0x85,
    ResponseOnEvent = 0x86,
    LinkControl = 0x87
}
