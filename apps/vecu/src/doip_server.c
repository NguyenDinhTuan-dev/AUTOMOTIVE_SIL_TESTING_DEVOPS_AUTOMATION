#include "doip_server.h"
#include "uds_driver.h"
#include <stdio.h>
#include <string.h>
#include <winsock2.h>
#include <stdarg.h>

static SOCKET g_serverSocket = INVALID_SOCKET;

static void PrintBoxLine(const char* format, ...) {
    char content[256];
    va_list args;
    va_start(args, format);
    vsnprintf(content, sizeof(content), format, args);
    va_end(args);
    printf("| %-70s |\n", content);
}

static void LogVisualFlow(
    uint8_t protocolVersion,
    uint16_t payloadType,
    uint16_t sourceAddr,
    uint16_t targetAddr,
    const uint8_t* reqUds,
    uint16_t reqUdsLen,
    const uint8_t* respUds,
    uint16_t respUdsLen
) {
    uint8_t sid = (reqUdsLen > 0) ? reqUds[0] : 0;
    
    // Static buffer holding last logged state to prevent console output spam
    static uint8_t lastDtcResp[64] = {0};
    static uint16_t lastDtcRespLen = 0;
    static uint16_t lastSpeed = 0xFFFF;
    static uint16_t lastRpm = 0xFFFF;
    static uint8_t lastTemp = 0xFF;
    
    if (sid == 0x19) {
        if (respUdsLen == lastDtcRespLen && memcmp(respUds, lastDtcResp, respUdsLen) == 0) {
            return; // Skip logging if DTC state unchanged (prevents console spam during Simulink polling)
        }
        if (respUdsLen < 64) {
            lastDtcRespLen = respUdsLen;
            memcpy(lastDtcResp, respUds, respUdsLen);
        }
    }

    if (sid == 0x2E) {
        uint16_t did = (reqUdsLen >= 3) ? ((reqUds[1] << 8) | reqUds[2]) : 0;
        if (did == 0x0100) {
            uint16_t val = (reqUdsLen >= 5) ? ((reqUds[3] << 8) | reqUds[4]) : 0;
            if (val == lastSpeed) return;
            lastSpeed = val;
        } else if (did == 0x0101) {
            uint16_t val = (reqUdsLen >= 5) ? ((reqUds[3] << 8) | reqUds[4]) : 0;
            if (val == lastRpm) return;
            lastRpm = val;
        } else if (did == 0x0102 || did == 0x0105) {
            uint8_t val = (reqUdsLen >= 4) ? reqUds[3] : 0;
            if (val == lastTemp) return;
            lastTemp = val;
        }
    }

    char reqHex[256] = {0};
    for (int i = 0; i < reqUdsLen && i < 30; i++) {
        sprintf(reqHex + strlen(reqHex), "%02X ", reqUds[i]);
    }
    
    char respHex[256] = {0};
    for (int i = 0; i < respUdsLen && i < 30; i++) {
        sprintf(respHex + strlen(respHex), "%02X ", respUds[i]);
    }

    sid = (reqUdsLen > 0) ? reqUds[0] : 0;
    const char* sidName = "Unknown";
    char opDesc[128] = "Idle";
    char valDesc[128] = "N/A";

    if (sid == 0x22) {
        sidName = "ReadDataByIdentifier";
        uint16_t did = (reqUdsLen >= 3) ? ((reqUds[1] << 8) | reqUds[2]) : 0;
        if (did == 0x0100) {
            sprintf(opDesc, "Reading Vehicle Speed from Virtual RAM");
            if (respUdsLen >= 5 && respUds[0] == 0x62) {
                uint16_t speed = (respUds[3] << 8) | respUds[4];
                sprintf(valDesc, "Speed = %u km/h", speed);
            }
        } else if (did == 0x0101) {
            sprintf(opDesc, "Reading Engine RPM from Virtual RAM");
            if (respUdsLen >= 5 && respUds[0] == 0x62) {
                uint16_t rpm = (respUds[3] << 8) | respUds[4];
                sprintf(valDesc, "RPM = %u RPM", rpm);
            }
        } else if (did == 0x0102 || did == 0x0105) {
            sprintf(opDesc, "Reading Coolant Temperature from Virtual RAM");
            if (respUdsLen >= 4 && respUds[0] == 0x62) {
                uint8_t temp = respUds[3];
                sprintf(valDesc, "Coolant Temp = %u C", temp);
            }
        } else if (did == 0xF190) {
            sprintf(opDesc, "Reading VIN (Vehicle Identification Number)");
            if (respUdsLen >= 4 && respUds[0] == 0x62) {
                char vin[32] = {0};
                int vinLen = 0;
                for (int i = 3; i < respUdsLen && vinLen < 31; i++) {
                    vin[vinLen++] = (char)respUds[i];
                }
                sprintf(valDesc, "VIN = \"%s\"", vin);
            }
        } else {
            sprintf(opDesc, "Reading Unknown DID 0x%04X", did);
        }
    } else if (sid == 0x2E) {
        sidName = "WriteDataByIdentifier";
        uint16_t did = (reqUdsLen >= 3) ? ((reqUds[1] << 8) | reqUds[2]) : 0;
        if (did == 0x0102 || did == 0x0105) {
            uint8_t val = (reqUdsLen >= 4) ? reqUds[3] : 0;
            sprintf(opDesc, "Writing Coolant Temp (Fault Injection)");
            sprintf(valDesc, "New Temp = %u C %s", val, (val >= 120) ? "(DTC P0115 Triggered!)" : "");
        } else if (did == 0x0100) {
            uint16_t val = (reqUdsLen >= 5) ? ((reqUds[3] << 8) | reqUds[4]) : 0;
            sprintf(opDesc, "Writing Vehicle Speed");
            sprintf(valDesc, "New Speed = %u km/h", val);
        } else if (did == 0x0101) {
            uint16_t val = (reqUdsLen >= 5) ? ((reqUds[3] << 8) | reqUds[4]) : 0;
            sprintf(opDesc, "Writing Engine RPM");
            sprintf(valDesc, "New RPM = %u RPM", val);
        } else {
            sprintf(opDesc, "Writing Unknown DID 0x%04X", did);
        }
    } else if (sid == 0x19) {
        sidName = "ReadDTCInformation";
        uint8_t sub = (reqUdsLen >= 2) ? reqUds[1] : 0;
        if (sub == 0x02) {
            sprintf(opDesc, "Reading Active DTCs");
            if (respUdsLen >= 7 && respUds[0] == 0x59) {
                uint32_t dtc = ((uint32_t)respUds[3] << 16) | ((uint32_t)respUds[4] << 8) | respUds[5];
                sprintf(valDesc, "Active DTC: 0x%06X (P0115 / Coolant Overheat)", dtc);
            } else if (respUdsLen >= 3 && respUds[0] == 0x59 && respUds[2] == 0x00) {
                sprintf(valDesc, "No Active DTCs");
            }
        } else {
            sprintf(opDesc, "Reading DTCs (SubFunction 0x%02X)", sub);
        }
    }

    // Check for negative response
    if (respUdsLen >= 3 && respUds[0] == 0x7F) {
        sprintf(valDesc, "ERROR: Negative Response! NRC 0x%02X", respUds[2]);
    }

    printf("\n");
    printf("+------------------------------------------------------------------------+\n");
    PrintBoxLine("[DoIP FLOW] DATA PROCESSING FLOW VISUALIZATION");
    printf("+------------------------------------------------------------------------+\n");
    PrintBoxLine("1. NETWORK LAYER (DoIP Packet Received)");
    PrintBoxLine("   +-- Protocol Version : 0x%02X", protocolVersion);
    PrintBoxLine("   +-- Payload Type     : 0x%04X (Diagnostic Message)", payloadType);
    PrintBoxLine("   +-- Source Address   : 0x%04X (Test Client)", sourceAddr);
    PrintBoxLine("   +-- Target Address   : 0x%04X (vECU Target)", targetAddr);
    PrintBoxLine("");
    PrintBoxLine("2. DIAGNOSTIC LAYER (UDS Extracted)");
    PrintBoxLine("   +-- Raw Request Hex  : %s", reqHex);
    PrintBoxLine("   +-- Service Name     : 0x%02X (%s)", sid, sidName);
    PrintBoxLine("   +-- Target DID/Sub   : 0x%04X", (sid == 0x19) ? (reqUdsLen >= 2 ? reqUds[1] : 0) : ((reqUdsLen >= 3) ? ((reqUds[1] << 8) | reqUds[2]) : 0));
    PrintBoxLine("");
    PrintBoxLine("3. APPLICATION LAYER (ASW & Virtual Memory)");
    PrintBoxLine("   +-- Operation        : %s", opDesc);
    PrintBoxLine("   +-- Result Value     : %s", valDesc);
    PrintBoxLine("");
    PrintBoxLine("4. RESPONSE GENERATION (DoIP Encapsulation)");
    PrintBoxLine("   +-- Raw Response Hex : %s", respHex);
    PrintBoxLine("   +-- Sent DoIP Frame  : %d bytes (swapped src/target address)", 8 + respUdsLen + 4);
    printf("+------------------------------------------------------------------------+\n");
    printf("\n");
    fflush(stdout);
}

void InitDoIPServer(uint16_t port) {
    // 1. Initialize TCP socket
    g_serverSocket = socket(AF_INET, SOCK_STREAM, 0);
    if (g_serverSocket == INVALID_SOCKET) {
        printf("-> [NETWORK_ERROR] Cannot create socket.\n");
        return;
    }

    // 2. Configure Localhost IP and Port
    struct sockaddr_in serverAddr;
    serverAddr.sin_family = AF_INET;
    serverAddr.sin_addr.s_addr = inet_addr("127.0.0.1");
    serverAddr.sin_port = htons(port);

    // 3. Bind socket
    if (bind(g_serverSocket, (struct sockaddr*)&serverAddr, sizeof(serverAddr)) == SOCKET_ERROR) {
        printf("-> [NETWORK_ERROR] Bind failed on port %d.\n", port);
        closesocket(g_serverSocket);
        return;
    }

    // 4. Listen for incoming connections
    if (listen(g_serverSocket, SOMAXCONN) == SOCKET_ERROR) {
        printf("-> [NETWORK_ERROR] Listen failed.\n");
        closesocket(g_serverSocket);
        return;
    }

    printf("-> [NETWORK] DoIP Server initialized and listening on 127.0.0.1:%d\n", port);
}

// Helper function to read exact number of bytes from TCP stream
static int ReadExact(SOCKET s, uint8_t* buf, int len) {
    int totalRead = 0;
    while (totalRead < len) {
        int bytesRead = recv(s, (char*)(buf + totalRead), len - totalRead, 0);
        if (bytesRead <= 0) {
            return -1; // Connection closed or error
        }
        totalRead += bytesRead;
    }
    return totalRead;
}

void RunDoIPServer(void) {
    SOCKET clientSockets[5];
    uint8_t clientActivated[5];
    for (int i = 0; i < 5; i++) {
        clientSockets[i] = INVALID_SOCKET;
        clientActivated[i] = 0;
    }

    fd_set readFds;
    uint8_t rxBuffer[2048];
    uint8_t txBuffer[2048];

    printf("-> [NETWORK] Running concurrent DoIP Server loop (select-based)...\n");

    while (1) {
        FD_ZERO(&readFds);
        FD_SET(g_serverSocket, &readFds);
        SOCKET maxSocket = g_serverSocket;

        for (int i = 0; i < 5; i++) {
            if (clientSockets[i] != INVALID_SOCKET) {
                FD_SET(clientSockets[i], &readFds);
                if (clientSockets[i] > maxSocket) {
                    maxSocket = clientSockets[i];
                }
            }
        }

        struct timeval timeout;
        timeout.tv_sec = 1;  // Wake up every 1 second to evaluate S3 Server Timeout
        timeout.tv_usec = 0;

        int activity = select((int)(maxSocket + 1), &readFds, NULL, NULL, &timeout);
        if (activity == SOCKET_ERROR) {
            printf("-> [NETWORK_ERROR] Select failed.\n");
            break;
        }

        // 1. Accept new connection
        if (FD_ISSET(g_serverSocket, &readFds)) {
            struct sockaddr_in clientAddr;
            int clientAddrLen = sizeof(clientAddr);
            SOCKET newSocket = accept(g_serverSocket, (struct sockaddr*)&clientAddr, &clientAddrLen);
            if (newSocket != INVALID_SOCKET) {
                int acceptedIndex = -1;
                for (int i = 0; i < 5; i++) {
                    if (clientSockets[i] == INVALID_SOCKET) {
                        clientSockets[i] = newSocket;
                        clientActivated[i] = 0; // Requires Routing Activation
                        acceptedIndex = i;
                        break;
                    }
                }
                if (acceptedIndex != -1) {
                    printf("-> [NETWORK] Accepted new connection at slot %d from %s:%d\n",
                           acceptedIndex, inet_ntoa(clientAddr.sin_addr), ntohs(clientAddr.sin_port));
                } else {
                    printf("-> [NETWORK_WARNING] Max clients reached. Rejecting connection.\n");
                    closesocket(newSocket);
                }
            }
        }

        // 2. Handle active client communication
        for (int i = 0; i < 5; i++) {
            SOCKET clientSocket = clientSockets[i];
            if (clientSocket != INVALID_SOCKET && FD_ISSET(clientSocket, &readFds)) {
                // Read exactly 8 bytes DoIP Header
                uint8_t header[8];
                if (ReadExact(clientSocket, header, 8) < 0) {
                    printf("-> [NETWORK] Client disconnected from slot %d.\n", i);
                    closesocket(clientSocket);
                    clientSockets[i] = INVALID_SOCKET;
                    clientActivated[i] = 0;
                    continue;
                }

                // Verify DoIP Protocol version
                if (header[0] != 0x02 || header[1] != 0xFD) {
                    printf("-> [NETWORK_ERROR] Invalid DoIP protocol signature. Closing client %d.\n", i);
                    closesocket(clientSocket);
                    clientSockets[i] = INVALID_SOCKET;
                    clientActivated[i] = 0;
                    continue;
                }

                uint16_t payloadType = (header[2] << 8) | header[3];
                uint32_t payloadLength = ((uint32_t)header[4] << 24) |
                                         ((uint32_t)header[5] << 16) |
                                         ((uint32_t)header[6] << 8)  |
                                         header[7];

                if (payloadLength > sizeof(rxBuffer)) {
                    printf("-> [NETWORK_ERROR] DoIP payload too large (%u bytes). Closing client %d.\n", payloadLength, i);
                    closesocket(clientSocket);
                    clientSockets[i] = INVALID_SOCKET;
                    clientActivated[i] = 0;
                    continue;
                }

                // Read exactly payloadLength bytes
                if (ReadExact(clientSocket, rxBuffer, payloadLength) < 0) {
                    printf("-> [NETWORK] Client disconnected from slot %d.\n", i);
                    closesocket(clientSocket);
                    clientSockets[i] = INVALID_SOCKET;
                    clientActivated[i] = 0;
                    continue;
                }

                // Process DoIP packet type
                if (payloadType == 0x0005) {
                    // Routing Activation Request (0x0005)
                    if (payloadLength >= 7) {
                        uint16_t testerAddress = (rxBuffer[0] << 8) | rxBuffer[1];
                        uint8_t activationType = rxBuffer[2];

                        clientActivated[i] = 1; // Activated
                        printf("-> [DoIP] Routing Activation successful for slot %d (Tester Address: 0x%04X, Type: 0x%02X).\n",
                               i, testerAddress, activationType);

                        // Send Routing Activation Response (0x0006) - 13 bytes payload (21 bytes total)
                        txBuffer[0] = 0x02;
                        txBuffer[1] = 0xFD;
                        txBuffer[2] = 0x00;
                        txBuffer[3] = 0x06;
                        txBuffer[4] = 0;
                        txBuffer[5] = 0;
                        txBuffer[6] = 0;
                        txBuffer[7] = 13; // Payload Length = 13

                        txBuffer[8] = rxBuffer[0];  // Tester logical address echoed
                        txBuffer[9] = rxBuffer[1];
                        txBuffer[10] = 0x0E;        // DoIP Entity Logical Address (vECU Target: 0x0E80)
                        txBuffer[11] = 0x80;
                        txBuffer[12] = 0x10;        // Response Code: 0x10 (Accepted)
                        txBuffer[13] = 0;           // Reserved (ISO) 4 bytes
                        txBuffer[14] = 0;
                        txBuffer[15] = 0;
                        txBuffer[16] = 0;
                        txBuffer[17] = 0;           // Reserved (OEM) 4 bytes
                        txBuffer[18] = 0;
                        txBuffer[19] = 0;
                        txBuffer[20] = 0;

                        send(clientSocket, (char*)txBuffer, 21, 0);
                    }
                } 
                else if (payloadType == 0x8001) {
                    // Diagnostic Message (0x8001)
                    if (!clientActivated[i]) {
                        printf("-> [DoIP_ERROR] Diagnostic message rejected: Routing not activated for client %d.\n", i);
                        closesocket(clientSocket);
                        clientSockets[i] = INVALID_SOCKET;
                        clientActivated[i] = 0;
                        continue;
                    }

                    if (payloadLength >= 4) {
                        uint16_t sourceAddr = (rxBuffer[0] << 8) | rxBuffer[1];
                        uint16_t targetAddr = (rxBuffer[2] << 8) | rxBuffer[3];
                        uint8_t* udsPayload = &rxBuffer[4];
                        uint16_t udsLen = payloadLength - 4;

                        uint8_t udsResponse[256];
                        uint16_t responseLen = ProcessUDSRequest(udsPayload, udsLen, udsResponse);

                        if (responseLen > 0) {
                            // 1. Send Diagnostic ACK (0x8002) - 5 bytes payload (13 bytes total)
                            txBuffer[0] = 0x02;
                            txBuffer[1] = 0xFD;
                            txBuffer[2] = 0x80;
                            txBuffer[3] = 0x02;
                            txBuffer[4] = 0;
                            txBuffer[5] = 0;
                            txBuffer[6] = 0;
                            txBuffer[7] = 5; // Payload length = 5 bytes

                            txBuffer[8] = rxBuffer[2];  // Target address from request (acts as source for ACK)
                            txBuffer[9] = rxBuffer[3];
                            txBuffer[10] = rxBuffer[0]; // Source address from request (acts as target for ACK)
                            txBuffer[11] = rxBuffer[1];
                            txBuffer[12] = 0x00;        // ACK Code = 0x00 (Positive ACK)

                            send(clientSocket, (char*)txBuffer, 13, 0);

                            // 2. Send Diagnostic Response Message (0x8001)
                            uint32_t respPayloadLen = 4 + responseLen;
                            txBuffer[0] = 0x02;
                            txBuffer[1] = 0xFD;
                            txBuffer[2] = 0x80;
                            txBuffer[3] = 0x01;
                            txBuffer[4] = (respPayloadLen >> 24) & 0xFF;
                            txBuffer[5] = (respPayloadLen >> 16) & 0xFF;
                            txBuffer[6] = (respPayloadLen >> 8) & 0xFF;
                            txBuffer[7] = respPayloadLen & 0xFF;

                            txBuffer[8] = rxBuffer[2];  // Source (target from request)
                            txBuffer[9] = rxBuffer[3];
                            txBuffer[10] = rxBuffer[0]; // Target (source from request)
                            txBuffer[11] = rxBuffer[1];

                            memcpy(&txBuffer[12], udsResponse, responseLen);
                            send(clientSocket, (char*)txBuffer, 8 + respPayloadLen, 0);

                            // Call visual flow logger
                            LogVisualFlow(
                                header[0],
                                payloadType,
                                sourceAddr,
                                targetAddr,
                                udsPayload,
                                udsLen,
                                udsResponse,
                                responseLen
                            );
                        }
                    }
                }
            }
        }

        // 3. Evaluate S3 Server Timeout after client polling cycle.
        if (g_activeSession == UDS_SESSION_EXTENDED) {
            time_t now = time(NULL);
            if (now - g_lastRequestTime >= 5) {
                g_activeSession = UDS_SESSION_DEFAULT;
                g_isSecurityUnlocked = 0;
                printf("-> [SECURITY] S3 Timeout (5s) reached. Reverted to Default Session & LOCKED.\n");
                fflush(stdout);
            }
        }
    }
}