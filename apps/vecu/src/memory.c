#include "memory.h"
#include <stdio.h>
#include <string.h>

// =====================================================================
// GLOBAL VIRTUAL RAM DECLARATIONS
// =====================================================================

static VehicleState_t g_vehicleState;
static DiagnosticTroubleCode_t g_dtcMemory[10]; // Maximum 10 active DTC entries
static uint8_t g_dtcCount = 0;                  // Active DTC count
static uint16_t g_simulinkUpdateCounter = 0;    // Simulink update cycle counter

// Non-Volatile Memory (NvM) binary persistence file handlers
static void SaveDTCsToDisk(void) {
    FILE* file = fopen("dtc_eeprom.bin", "wb");
    if (file) {
        fwrite(&g_dtcCount, sizeof(g_dtcCount), 1, file);
        if (g_dtcCount > 0) {
            fwrite(g_dtcMemory, sizeof(DiagnosticTroubleCode_t), g_dtcCount, file);
        }
        fclose(file);
        printf("-> [NvM] DTC memory saved persistently to dtc_eeprom.bin.\n");
    } else {
        printf("-> [NvM_ERROR] Failed to save DTC memory to disk.\n");
    }
}

static void LoadDTCsFromDisk(void) {
    FILE* file = fopen("dtc_eeprom.bin", "rb");
    if (file) {
        if (fread(&g_dtcCount, sizeof(g_dtcCount), 1, file) == 1) {
            if (g_dtcCount > 10) g_dtcCount = 10;
            if (g_dtcCount > 0) {
                fread(g_dtcMemory, sizeof(DiagnosticTroubleCode_t), g_dtcCount, file);
            }
            printf("-> [NvM] Loaded %d active DTC(s) from dtc_eeprom.bin successfully.\n", g_dtcCount);
        } else {
            g_dtcCount = 0;
            memset(g_dtcMemory, 0, sizeof(g_dtcMemory));
        }
        fclose(file);
    } else {
        g_dtcCount = 0;
        memset(g_dtcMemory, 0, sizeof(g_dtcMemory));
        printf("-> [NvM] No persistent dtc_eeprom.bin file found. Initialized empty DTC memory.\n");
    }
}

// =====================================================================
// SUBSYSTEM IMPLEMENTATION
// =====================================================================

void InitVirtualMemory(void) {
    // Initialize default vehicle state upon ignition power-on
    g_vehicleState.vehicleSpeed = 0;
    g_vehicleState.engineRPM = 800;    // Idle engine speed (RPM)
    g_vehicleState.coolantTemp = 90;   // Normal operating coolant temp (90 °C)
    g_simulinkUpdateCounter = 0;       // Reset Simulink update counter
    
    LoadDTCsFromDisk(); // Reload persisted DTC records from NvM binary file
    
    printf("-> [MEMORY] Virtual Memory initialized successfully.\n");
    printf("-> [NvM] Non-Volatile Memory and version check loaded.\n");
}

uint16_t ReadVehicleSpeed(void) {
    return g_vehicleState.vehicleSpeed;
}

uint16_t ReadEngineRPM(void) {
    return g_vehicleState.engineRPM;
}

uint8_t ReadCoolantTemp(void) {
    return g_vehicleState.coolantTemp;
}

void SetCoolantTemp(uint8_t temp) {
    g_vehicleState.coolantTemp = temp;
    IncrementSimulinkUpdateCounter(); // Increment update counter
    
    // Application Software (ASW) Monitor Logic:
    // Automatic diagnostic event monitoring: Trigger fault if coolant temp >= 120 °C
    if (temp >= 120) {
        // Log over-temperature event edge trigger once to prevent console chattering
        if (GetDTCStatus(0x000115) == 0x00) {
            SetDTC(0x000115, 0x01); // Set DTC 0x000115 (P0115 Coolant Overheat) active
            printf("-> [ASW_WARNING] Coolant Temp too high (%d C)! DTC 0x000115 triggered.\n", temp);
        }
    } else if (temp <= 100) {
        // Automatic fault clearance when coolant temperature drops below 100 °C (Hysteresis cooling filter)
        if (GetDTCStatus(0x000115) == 0x01) {
            SetDTC(0x000115, 0x00); // Set DTC status inactive
            printf("-> [ASW_INFO] Coolant Temp cooled down below safe threshold (%d C). DTC 0x000115 set to Inactive.\n", temp);
        }
    }
}

void SetVehicleSpeed(uint16_t speed) {
    g_vehicleState.vehicleSpeed = speed;
    IncrementSimulinkUpdateCounter(); // Increment update counter
}

void SetEngineRPM(uint16_t rpm) {
    g_vehicleState.engineRPM = rpm;
    IncrementSimulinkUpdateCounter(); // Increment update counter
}

uint16_t GetSimulinkUpdateCounter(void) {
    return g_simulinkUpdateCounter;
}

void IncrementSimulinkUpdateCounter(void) {
    g_simulinkUpdateCounter++;
}

void SetDTC(uint32_t code, uint8_t status) {
    // Check if DTC already exists in memory array to update status byte
    for (uint8_t i = 0; i < g_dtcCount; i++) {
        if (g_dtcMemory[i].dtcCode == code) {
            if (g_dtcMemory[i].statusMask != status) {
                g_dtcMemory[i].statusMask = status;
                SaveDTCsToDisk(); // Persist changes to NvM file upon status update
            }
            return;
        }
    }
    
    // Append new DTC entry if memory capacity permits
    if (g_dtcCount < 10) {
        g_dtcMemory[g_dtcCount].dtcCode = code;
        g_dtcMemory[g_dtcCount].statusMask = status;
        g_dtcCount++;
        SaveDTCsToDisk(); // Persist changes to NvM file upon new entry
    }
}

uint8_t GetDTCStatus(uint32_t code) {
    // Search DTC memory array for requested code status
    for (uint8_t i = 0; i < g_dtcCount; i++) {
        if (g_dtcMemory[i].dtcCode == code) {
            return g_dtcMemory[i].statusMask;
        }
    }
    return 0x00; // Return inactive status 0x00 if DTC not found
}

void ClearDTCs(void) {
    g_dtcCount = 0;
    memset(g_dtcMemory, 0, sizeof(g_dtcMemory));
    SaveDTCsToDisk(); // Overwrite NvM binary file with empty array
    printf("-> [MEMORY] Virtual DTC memory cleared successfully.\n");
}