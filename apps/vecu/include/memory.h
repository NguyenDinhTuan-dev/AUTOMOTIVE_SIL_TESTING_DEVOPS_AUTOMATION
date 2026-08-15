#ifndef MEMORY_H
#define MEMORY_H

#include <stdint.h>

// Vehicle operating state data structure (Virtual RAM representation)
typedef struct {
    uint16_t vehicleSpeed;    // Vehicle Speed (km/h)
    uint16_t engineRPM;       // Engine Speed (RPM)
    uint8_t  coolantTemp;     // Engine Coolant Temperature (°C)
} VehicleState_t;

// Diagnostic Trouble Code (DTC) entry structure
typedef struct {
    uint32_t dtcCode;         // DTC Code (e.g., 0x000115 for P0115 Coolant Overheat)
    uint8_t  statusMask;      // DTC Status Mask (0x01: Active, 0x00: Inactive)
} DiagnosticTroubleCode_t;

// =====================================================================
// Virtual Memory API Interfaces
// =====================================================================

// Initializes virtual memory subsystem upon vECU boot
void InitVirtualMemory(void);

// Parameter read interfaces (Invoked by UDS Service 0x22)
uint16_t ReadVehicleSpeed(void);
uint16_t ReadEngineRPM(void);
uint8_t  ReadCoolantTemp(void);

// Parameter write interfaces (Used for Fault Injection & Simulink updates)
void SetCoolantTemp(uint8_t temp);
void SetVehicleSpeed(uint16_t speed);
void SetEngineRPM(uint16_t rpm);

// Diagnostic Trouble Code (DTC) management APIs (Invoked by UDS Service 0x19)
void SetDTC(uint32_t code, uint8_t status);
uint8_t GetDTCStatus(uint32_t code);
void ClearDTCs(void);

// Simulink update cycle counter tracking (DID 0x01FF)
uint16_t GetSimulinkUpdateCounter(void);
void IncrementSimulinkUpdateCounter(void);

#endif // MEMORY_H