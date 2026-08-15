namespace AutVecu.Cores.Enums;

public enum UdsReadDtcReportType : byte
{
    ReportNumberOfDtcByStatusMask = 0x01,
    ReportDtcByStatusMask = 0x02,
    ReportMirrorMemoryDtcByStatusMask = 0x0F,
    ReportNumberOfMirrorMemoryDtcByStatusMask = 0x11,
    ReportNumberOfEmissionsObdDtcByStatusMask = 0x12,
    ReportEmissionsObdDtcByStatusMask = 0x13,
    ReportSupportedDtc = 0x0A,
    ReportFirstTestFailedDtc = 0x0B,
    ReportFirstConfirmedDtc = 0x0C,
    ReportMostRecentTestFailedDtc = 0x0D,
    ReportMostRecentConfirmedDtc = 0x0E,
    ReportDtcFaultDetectionCounter = 0x14,
    ReportDtcWithPermanentStatus = 0x15
}
