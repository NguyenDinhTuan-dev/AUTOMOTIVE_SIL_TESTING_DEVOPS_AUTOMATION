#ifndef UDS_DRIVER_H
#define UDS_DRIVER_H

#include <stdint.h>
#include <time.h>

// Standard UDS Service Identifiers (SIDs)
#define UDS_SID_DIAGNOSTIC_SESSION_CONTROL 0x10
#define UDS_SID_READ_DATA_BY_IDENTIFIER  0x22
#define UDS_SID_READ_DTC_INFORMATION     0x19
#define UDS_SID_WRITE_DATA_BY_IDENTIFIER 0x2E
#define UDS_SID_SECURITY_ACCESS          0x27
#define UDS_SID_CLEAR_DIAGNOSTIC_INFORMATION 0x14
#define UDS_SID_TESTER_PRESENT           0x3E

// UDS Diagnostic Session Definitions
#define UDS_SESSION_DEFAULT              0x01
#define UDS_SESSION_EXTENDED             0x03

// UDS Negative Response Codes (NRCs)
#define UDS_NRC_SERVICE_NOT_SUPPORTED    0x11
#define UDS_NRC_SUBFUNCTION_NOT_SUPPORTED 0x12
#define UDS_NRC_INCORRECT_MESSAGE_LENGTH 0x13
#define UDS_NRC_REQUEST_OUT_OF_RANGE     0x31
#define UDS_NRC_SECURITY_ACCESS_DENIED   0x33
#define UDS_NRC_INVALID_KEY              0x35
#define UDS_NRC_SERVICE_NOT_SUPPORTED_IN_ACTIVE_SESSION     0x7F
#define UDS_NRC_SUBFUNCTION_NOT_SUPPORTED_IN_ACTIVE_SESSION  0x7E

// Vehicle Data Identifiers (DIDs)
#define UDS_DID_VEHICLE_SPEED            0x0100
#define UDS_DID_ENGINE_RPM               0x0101
#define UDS_DID_COOLANT_TEMP             0x0102

// Global diagnostic session and security state variables
extern uint8_t g_activeSession;
extern uint8_t g_isSecurityUnlocked;
extern time_t g_lastRequestTime;

// =====================================================================
// UDS Protocol Handler APIs
// =====================================================================

// Parses incoming UDS request frame from Test Runner / Client
// Returns: Payload byte length of the diagnostic response frame
uint16_t ProcessUDSRequest(const uint8_t* requestBuffer, uint16_t requestLen, uint8_t* responseBuffer);
void InitSecurity(void);

#endif // UDS_DRIVER_H