#ifndef DOIP_SERVER_H
#define DOIP_SERVER_H

#include <stdint.h>

// =====================================================================
// DoIP Network Server API Definitions
// =====================================================================

// Initializes the DoIP Server on the specified port (e.g., 13400).
void InitDoIPServer(uint16_t port);

// Runs the main vECU runtime event loop.
void RunDoIPServer(void);

#endif // DOIP_SERVER_H