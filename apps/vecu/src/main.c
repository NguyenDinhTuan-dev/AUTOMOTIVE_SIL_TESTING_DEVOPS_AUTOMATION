#include <stdio.h>
#include <winsock2.h>
#include <stdlib.h>
#include <time.h>
#include "memory.h"       // Virtual RAM and NvM memory manager interface
#include "doip_server.h"  // DoIP transport layer interface
#include "uds_driver.h"

#pragma comment(lib, "ws2_32.lib")

int main() {
    // Seed pseudo-random number generator for Diagnostic Security Access Challenge
    srand((unsigned int)time(NULL));

    // 1. Initialize Winsock stack (Windows Sockets API)
    WSADATA wsaData;
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
        printf("-> [SYSTEM_ERROR] Failed to initialize Winsock.\n");
        return 0;
    }
    printf("-> [SYSTEM] Winsock initialized successfully.\n");

    // 2. Initialize vECU core subsystems
    InitVirtualMemory();
    InitSecurity();
    InitDoIPServer(13400); // Standard DoIP TCP Port ISO 13400

    // 3. Execute main event loop
    RunDoIPServer();

    // 4. Cleanup Winsock stack upon exit
    WSACleanup();
    return 0;
}
