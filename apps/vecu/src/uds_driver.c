#include "uds_driver.h"
#include "memory.h"
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <time.h>

// Global diagnostic session and security access state variables
uint8_t g_activeSession = UDS_SESSION_DEFAULT;
uint8_t g_isSecurityUnlocked = 0;
time_t g_lastRequestTime = 0;

static uint16_t g_currentSeed = 0x0000;

void InitSecurity(void) {
    g_isSecurityUnlocked = 0;
    g_activeSession = UDS_SESSION_DEFAULT;
    g_currentSeed = 0x1234; // Initial default security seed
    g_lastRequestTime = time(NULL);
    printf("-> [SECURITY] Diagnostic security module initialized. Session: DEFAULT, Status: LOCKED.\n");
    fflush(stdout);
}

// Helper function constructing standard UDS Negative Response (NRC) frame
static uint16_t MakeNegativeResponse(uint8_t sid, uint8_t nrc, uint8_t* responseBuffer) {
    responseBuffer[0] = 0x7F; // UDS Negative Response Service ID prefix
    responseBuffer[1] = sid;  // Requested Service ID
    responseBuffer[2] = nrc;  // Negative Response Code (NRC)
    return 3;                 // Standard NRC payload length is 3 bytes
}

uint16_t ProcessUDSRequest(const uint8_t* requestBuffer, uint16_t requestLen, uint8_t* responseBuffer) {
    if (requestLen == 0 || requestBuffer == NULL || responseBuffer == NULL) {
        return 0;
    }

    // Refresh last valid request timestamp to maintain S3 session timer
    g_lastRequestTime = time(NULL);

    uint8_t sid = requestBuffer[0]; // First payload byte represents UDS Service ID (SID)

    switch (sid) {
        // =============================================================
        // SERVICE 0x10: DIAGNOSTIC SESSION CONTROL
        // =============================================================
        case UDS_SID_DIAGNOSTIC_SESSION_CONTROL: {
            if (requestLen < 2) {
                return MakeNegativeResponse(sid, UDS_NRC_INCORRECT_MESSAGE_LENGTH, responseBuffer);
            }
            uint8_t subFunc = requestBuffer[1];
            if (subFunc == UDS_SESSION_DEFAULT) { // 0x01
                g_activeSession = UDS_SESSION_DEFAULT;
                g_isSecurityUnlocked = 0; // Automatically lock security when returning to Default Session
                printf("-> [SESSION] Shifted to Default Session (10 01). Security LOCKED.\n");
                fflush(stdout);
            } else if (subFunc == UDS_SESSION_EXTENDED) { // 0x03
                g_activeSession = UDS_SESSION_EXTENDED;
                printf("-> [SESSION] Shifted to Extended Session (10 03). Ready for Security Access.\n");
                fflush(stdout);
            } else {
                return MakeNegativeResponse(sid, UDS_NRC_SUBFUNCTION_NOT_SUPPORTED, responseBuffer);
            }
            responseBuffer[0] = sid + 0x40; // 0x10 + 0x40 = 0x50
            responseBuffer[1] = subFunc;
            return 2;
        }

        // =============================================================
        // SERVICE 0x22: READ DATA BY IDENTIFIER (DID)
        // =============================================================
        case UDS_SID_READ_DATA_BY_IDENTIFIER: {
            if (requestLen < 3) {
                return MakeNegativeResponse(sid, UDS_NRC_INCORRECT_MESSAGE_LENGTH, responseBuffer);
            }

            uint16_t did = (requestBuffer[1] << 8) | requestBuffer[2];
            
            responseBuffer[0] = sid + 0x40;       // 0x22 + 0x40 = 0x62
            responseBuffer[1] = requestBuffer[1]; // Echo DID high byte
            responseBuffer[2] = requestBuffer[2]; // Echo DID low byte

            if (did == UDS_DID_VEHICLE_SPEED) {
                uint16_t speed = ReadVehicleSpeed();
                responseBuffer[3] = (speed >> 8) & 0xFF;
                responseBuffer[4] = speed & 0xFF;
                return 5;
            } 
            else if (did == UDS_DID_ENGINE_RPM) {
                uint16_t rpm = ReadEngineRPM();
                responseBuffer[3] = (rpm >> 8) & 0xFF;
                responseBuffer[4] = rpm & 0xFF;
                return 5;
            } 
            else if (did == UDS_DID_COOLANT_TEMP || did == 0x0105) {
                uint8_t temp = ReadCoolantTemp();
                responseBuffer[3] = temp;
                return 4;
            }
            else if (did == 0xF190) { // VIN DID
                responseBuffer[3] = 0x41; // 'A'
                responseBuffer[4] = 0x55; // 'U'
                responseBuffer[5] = 0x54; // 'T'
                responseBuffer[6] = 0x56; // 'V'
                responseBuffer[7] = 0x45; // 'E'
                responseBuffer[8] = 0x43; // 'C'
                responseBuffer[9] = 0x55; // 'U'
                return 10;
            }
            else if (did == 0x01FF) { // Simulink update counter
                uint16_t counter = GetSimulinkUpdateCounter();
                responseBuffer[3] = (counter >> 8) & 0xFF;
                responseBuffer[4] = counter & 0xFF;
                return 5;
            }
            else {
                return MakeNegativeResponse(sid, UDS_NRC_REQUEST_OUT_OF_RANGE, responseBuffer);
            }
        }

        // =============================================================
        // SERVICE 0x19: READ DTC INFORMATION
        // =============================================================
        case UDS_SID_READ_DTC_INFORMATION: {
            if (requestLen < 2) {
                return MakeNegativeResponse(sid, UDS_NRC_INCORRECT_MESSAGE_LENGTH, responseBuffer);
            }

            uint8_t subFunction = requestBuffer[1];
            
            if (subFunction == 0x02) {
                responseBuffer[0] = sid + 0x40; // 0x59
                responseBuffer[1] = subFunction;
                
                uint8_t status = GetDTCStatus(0x000115);
                
                if (status == 0x01) {
                    responseBuffer[2] = 0x01; // 1 active DTC
                    responseBuffer[3] = (0x000115 >> 16) & 0xFF;
                    responseBuffer[4] = (0x000115 >> 8) & 0xFF;
                    responseBuffer[5] = 0x000115 & 0xFF;
                    responseBuffer[6] = status;
                    return 7;
                } else {
                    responseBuffer[2] = 0x00;
                    return 3;
                }
            } else {
                return MakeNegativeResponse(sid, UDS_NRC_REQUEST_OUT_OF_RANGE, responseBuffer);
            }
        }

        // =============================================================
        // SERVICE 0x27: SECURITY ACCESS
        // =============================================================
        case UDS_SID_SECURITY_ACCESS: {
            if (requestLen < 2) {
                return MakeNegativeResponse(sid, UDS_NRC_INCORRECT_MESSAGE_LENGTH, responseBuffer);
            }

            // Extended Session is a mandatory prerequisite for Service 0x27
            if (g_activeSession != UDS_SESSION_EXTENDED) {
                return MakeNegativeResponse(sid, UDS_NRC_SERVICE_NOT_SUPPORTED_IN_ACTIVE_SESSION, responseBuffer);
            }

            uint8_t subFunc = requestBuffer[1];
            if (subFunc == 0x01) { // Request Seed
                // Generate pseudo-random non-zero security seed challenge
                g_currentSeed = (uint16_t)((rand() % 0xFFFE) + 1);
                responseBuffer[0] = sid + 0x40; // 0x67
                responseBuffer[1] = subFunc;    // 0x01
                responseBuffer[2] = (g_currentSeed >> 8) & 0xFF;
                responseBuffer[3] = g_currentSeed & 0xFF;
                printf("-> [SECURITY] Seed requested. Sent random: 0x%04X\n", g_currentSeed);
                fflush(stdout);
                return 4;
            } else if (subFunc == 0x02) { // Send Key
                if (requestLen < 4) {
                    return MakeNegativeResponse(sid, UDS_NRC_INCORRECT_MESSAGE_LENGTH, responseBuffer);
                }
                uint16_t sentKey = (requestBuffer[2] << 8) | requestBuffer[3];
                uint16_t expectedKey = (g_currentSeed ^ 0x5A5A) + 0x1234;
                if (sentKey == expectedKey) {
                    g_isSecurityUnlocked = 1;
                    responseBuffer[0] = sid + 0x40; // 0x67
                    responseBuffer[1] = subFunc;    // 0x02
                    printf("-> [SECURITY] Correct Key: 0x%04X. UNLOCKED.\n", sentKey);
                    fflush(stdout);
                    return 2;
                } else {
                    printf("-> [SECURITY] Invalid Key: 0x%04X (Expected: 0x%04X). Denied.\n", sentKey, expectedKey);
                    fflush(stdout);
                    return MakeNegativeResponse(sid, UDS_NRC_INVALID_KEY, responseBuffer);
                }
            } else {
                return MakeNegativeResponse(sid, UDS_NRC_SUBFUNCTION_NOT_SUPPORTED, responseBuffer);
            }
        }

        // =============================================================
        // SERVICE 0x14: CLEAR DIAGNOSTIC INFORMATION
        // =============================================================
        case UDS_SID_CLEAR_DIAGNOSTIC_INFORMATION: {
            if (requestLen < 4) {
                return MakeNegativeResponse(sid, UDS_NRC_INCORRECT_MESSAGE_LENGTH, responseBuffer);
            }
            // Validate security access privileges
            if (g_activeSession != UDS_SESSION_EXTENDED) {
                return MakeNegativeResponse(sid, UDS_NRC_SERVICE_NOT_SUPPORTED_IN_ACTIVE_SESSION, responseBuffer);
            }
            if (!g_isSecurityUnlocked) {
                return MakeNegativeResponse(sid, UDS_NRC_SECURITY_ACCESS_DENIED, responseBuffer);
            }
            ClearDTCs();
            responseBuffer[0] = sid + 0x40; // 0x54
            return 1;
        }

        // =============================================================
        // SERVICE 0x2E: WRITE DATA BY IDENTIFIER (FAULT INJECTION)
        // =============================================================
        case UDS_SID_WRITE_DATA_BY_IDENTIFIER: {
            if (requestLen < 4) {
                return MakeNegativeResponse(sid, UDS_NRC_INCORRECT_MESSAGE_LENGTH, responseBuffer);
            }
            // Validate security access privileges
            if (g_activeSession != UDS_SESSION_EXTENDED) {
                return MakeNegativeResponse(sid, UDS_NRC_SERVICE_NOT_SUPPORTED_IN_ACTIVE_SESSION, responseBuffer);
            }
            if (!g_isSecurityUnlocked) {
                return MakeNegativeResponse(sid, UDS_NRC_SECURITY_ACCESS_DENIED, responseBuffer);
            }

            uint16_t did = (requestBuffer[1] << 8) | requestBuffer[2];
            
            if (did == UDS_DID_COOLANT_TEMP || did == 0x0105) {
                uint8_t temp = requestBuffer[3];
                SetCoolantTemp(temp);
                
                responseBuffer[0] = sid + 0x40;       // 0x6E
                responseBuffer[1] = requestBuffer[1];
                responseBuffer[2] = requestBuffer[2];
                return 3;
            } else if (did == UDS_DID_VEHICLE_SPEED) {
                if (requestLen < 5) {
                    return MakeNegativeResponse(sid, UDS_NRC_INCORRECT_MESSAGE_LENGTH, responseBuffer);
                }
                uint16_t speed = (requestBuffer[3] << 8) | requestBuffer[4];
                SetVehicleSpeed(speed);
                responseBuffer[0] = sid + 0x40;
                responseBuffer[1] = requestBuffer[1];
                responseBuffer[2] = requestBuffer[2];
                return 3;
            } else if (did == UDS_DID_ENGINE_RPM) {
                if (requestLen < 5) {
                    return MakeNegativeResponse(sid, UDS_NRC_INCORRECT_MESSAGE_LENGTH, responseBuffer);
                }
                uint16_t rpm = (requestBuffer[3] << 8) | requestBuffer[4];
                SetEngineRPM(rpm);
                responseBuffer[0] = sid + 0x40;
                responseBuffer[1] = requestBuffer[1];
                responseBuffer[2] = requestBuffer[2];
                return 3;
            } else {
                return MakeNegativeResponse(sid, UDS_NRC_REQUEST_OUT_OF_RANGE, responseBuffer);
            }
        }

        // =============================================================
        // SERVICE 0x3E: TESTER PRESENT
        // =============================================================
        case UDS_SID_TESTER_PRESENT: {
            if (requestLen < 2) {
                return MakeNegativeResponse(sid, UDS_NRC_INCORRECT_MESSAGE_LENGTH, responseBuffer);
            }
            uint8_t subFunc = requestBuffer[1];
            if (subFunc == 0x00) {
                responseBuffer[0] = sid + 0x40; // 0x7E
                responseBuffer[1] = subFunc;    // 0x00
                return 2;
            } else if (subFunc == 0x80) {
                // Return 0 to suppress positive response message
                return 0;
            } else {
                return MakeNegativeResponse(sid, UDS_NRC_SUBFUNCTION_NOT_SUPPORTED, responseBuffer);
            }
        }

        // =============================================================
        // UNHANDLED SERVICE ID ROUTINE: RETURN NRC 0x11 (ServiceNotSupported)
        // =============================================================
        default:
            return MakeNegativeResponse(sid, UDS_NRC_SERVICE_NOT_SUPPORTED, responseBuffer);
    }
}