# vECU Automated Diagnostic Testing Platform
> **An Automated Software-in-the-Loop (SIL) Diagnostic Validation Framework for Automotive Virtual ECUs**

[![C Standard](https://img.shields.io/badge/Language-C18-blue.svg)](https://en.wikipedia.org/wiki/C11_(C_standard_revision))
[![AUTOSAR](https://img.shields.io/badge/Architecture-AUTOSAR%20Classic%20BSW-orange.svg)](https://www.autosar.org/)
[![DoIP Protocol](https://img.shields.io/badge/Transport-DoIP%20ISO%2013400-brightgreen.svg)](https://www.iso.org/standard/66538.html)
[![UDS Protocol](https://img.shields.io/badge/Diagnostics-UDS%20ISO%2014229-green.svg)](https://www.iso.org/standard/72439.html)
[![Simulink](https://img.shields.io/badge/Co--Simulation-MATLAB%2FSimulink%2010Hz-blueviolet.svg)](https://www.mathworks.com/products/simulink.html)
[![CI/CD](https://img.shields.io/badge/DevOps-Jenkins%20CI%2FCD-red.svg)](https://www.jenkins.io/)
[![C# WPF](https://img.shields.io/badge/Desktop%20UI-C%23%20.NET%20WPF-purple.svg)](https://dotnet.microsoft.com/)

---

## 📌 Executive Summary & Abstract

Modern automotive systems rely on complex networks of Electronic Control Units (ECUs) communicating over high-bandwidth Automotive Ethernet networks. Traditional hardware-based diagnostic validation (using physical testbenches or Hardware-in-the-Loop HIL rigs) suffers from high equipment costs (often exceeding $10,000 per seat), inflexibility, and potential safety hazards when injecting extreme fault conditions.

This project introduces an **Automated Software-in-the-Loop (SIL) Diagnostic Validation Platform** that virtualizes an engine-control virtual ECU (**vECU**) within a 100% software-based environment. By bridging native C-code AUTOSAR Basic Software (BSW) abstractions with a 10 Hz MATLAB/Simulink closed-loop thermal plant model, a C# WPF desktop control interface, and a 6-stage Jenkins CI/CD pipeline, the platform enables repeatable, automated, and hardware-free diagnostic verification early in the software development lifecycle (Shift-Left Testing).

---

## 🚀 Key Technical Innovations & Unique Selling Points (USPs)

1. **Zero-Hardware Cost SIL Virtualization ($0 Equipment Overhead)**
   - Replaces physical Ethernet interfaces (e.g., Vector VN5640/VN1630) by routing **Diagnostics over IP (ISO 13400-2)** over local TCP/IP port 13400.
   - Allows embedded software developers and test engineers to validate DoIP and UDS stacks on standard PCs without hardware dependencies.

2. **10 Hz Closed-Loop Simulink Co-Simulation & FiM Limp-Home Mode**
   - Connects the vECU core to a MATLAB/Simulink thermal dynamics model in real-time.
   - When Simulated Coolant Temperature reaches **≥ 120 °C**, DEM triggers Diagnostic Trouble Code **DTC 0x000115 (P0115 Coolant Overheat)**, persists FreezeFrame data to binary EEPROM (`dtc_eeprom.bin`), and the **Function Inhibition Manager (FiM)** restricts throttle position to **30% (Limp-Home Safe Mode)**.
   - Incorporates a **Hysteresis Cooling Filter** (requiring temperature to drop below **≤ 100 °C** to unlatch fault states) to prevent throttle chattering.

3. **6-Stage Automated Jenkins CI/CD Quality Gate**
   - Automates regression testing whenever source code changes are pushed to Git.
   - Pipeline stages: **Checkout ➔ Compile (GCC / .NET) ➔ Deploy vECU ➔ Run Tcl Regression Suite ➔ Terminate Process / Release Socket ➔ Archive Build Evidence**.

4. **Native AUTOSAR Classic BSW Architecture in C**
   - Implements modular C-code modules strictly modeling AUTOSAR Classic BSW layers:
     - **DCM (Diagnostic Communication Manager)**: Handles DSL session/security state machines, DSD service dispatching, and DSP handler routines.
     - **DEM (Diagnostic Event Manager)**: Manages DTC status bytes and fault event filtering.
     - **NvM (Non-Volatile Memory)**: Handles EEPROM file persistence.
     - **FiM (Function Inhibition Manager)**: Evaluates safety permission states.

5. **Hazardous Fault Injection Without Physical Risks**
   - Enables test engineers to safely simulate extreme fault conditions (over-temperature, voltage drop, S3 session timeouts) without risk of damaging physical engines or testbench hardware.

---

## 🏛️ System Architecture Specifications

The system is structured into four highly synchronized layers connected over localhost socket port 13400:

```text
+-----------------------------------------------------------------------------------+
| LAYER 1: TEST AUTOMATION & USER INTERFACE                                         |
|  - Jenkins CI/CD Pipeline (Declarative Jenkinsfile)                               |
|  - Tcl Automated Regression Test Suite (VTC_01 to VTC_05)                        |
|  - C# WPF Desktop Control Panel Dashboard (MVVM Architecture)                     |
+-----------------------------------------------------------------------------------+
                                      |  (TCP Socket Port 13400)
                                      v
+-----------------------------------------------------------------------------------+
| LAYER 2: TRANSPORT & ROUTING LAYER (DoIP ISO 13400-2)                             |
|  - TCP/IP Loopback Socket Server (select() non-blocking event loop)               |
|  - DoIP Routing Activation Handshake (0x0005 Request / 0x0006 Response)           |
|  - Logical Address Routing (Tester 0x00E0 -> vECU 0x1001)                         |
+-----------------------------------------------------------------------------------+
                                      |
                                      v
+-----------------------------------------------------------------------------------+
| LAYER 3: AUTOSAR BSW DIAGNOSTIC ENGINE & EVENT MEMORY                             |
|  - DCM (Diagnostic Communication Manager): UDS SIDs 0x10, 0x27, 0x22, 0x2E, 0x19, 0x14 |
|  - DEM (Diagnostic Event Manager): DTC 0x000115 (P0115 Coolant Overheat)           |
|  - NvM (Non-Volatile Memory Manager): Persistence file `dtc_eeprom.bin`           |
|  - FiM (Function Inhibition Manager): Safety Throttle Restriction to 30%          |
+-----------------------------------------------------------------------------------+
                                      |  (10 Hz Co-Simulation Bridge)
                                      v
+-----------------------------------------------------------------------------------+
| LAYER 4: CLOSED-LOOP VEHICLE PLANT SIMULATION                                     |
|  - MATLAB / Simulink Powertrain Engine Model (100ms fixed-step solver)            |
|  - Thermal & Speed Dynamics Feedback Loop (`send_to_vecu.m`)                       |
+-----------------------------------------------------------------------------------+
```

---

## 📜 Detailed UDS Diagnostic Service Specifications

| Service ID (SID) | Service Name | Technical Details & Execution Logic |
| :-: | :--- | :--- |
| `0x10` | DiagnosticSessionControl | `0x01` Default Session, `0x03` Extended Session (Mandatory for privilege operations). |
| `0x27` | SecurityAccess | Challenge-Response handshake: Request Seed (`27 01`) returns pseudo-random seed `0x6543`. Send Key (`27 02 [Key]`) validates response against `Key = (Seed ^ 0x5A5A) + 0x1234`. |
| `0x22` | ReadDataByIdentifier | Reads simulated parameters: Vehicle Speed (`0x0100`), RPM (`0x0101`), Coolant Temp (`0x0102`), VIN (`0xF190`), Simulink Update Counter (`0x01FF`). |
| `0x2E` | WriteDataByIdentifier | Writes simulated parameters & injects simulated over-temperature coolant fault (`0x0105`). |
| `0x19` | ReadDTCInformation | Queries active fault memory and status mask for DTC `0x000115` (P0115 Coolant Overheat). |
| `0x14` | ClearDiagnosticInformation | Clears active DTC memory array and resets persisted EEPROM binary file `dtc_eeprom.bin`. |
| `0x3E` | TesterPresent | Keep-alive ping (`3E 80` Suppress Positive Response) resetting the 5.0s S3 Server Timeout timer. |

---

## 📄 Technical Documentation & White Paper Report

The complete academic white paper and technical architecture report for this independent collaborative project is available in the root repository:

* 📖 **[AUTOMOTIVE_SIL_TESTING_DEVOPS_AUTOMATION.pdf](AUTOMOTIVE_SIL_TESTING_DEVOPS_AUTOMATION.pdf)** — *Full Technical Specification, System Architecture, DoIP/UDS Protocol Specification, and Verification Results Report.*

---

## 📁 Repository Structure

```text
vECU_Automated_Testing_Framework/
│
├── .vscode/                          # VS Code build tasks and settings
│   └── tasks.json                    # GCC build task linking -lws2_32
│
├── apps/
│   ├── vecu/                         # App 1: vECU Native C Core (Embedded Subsystem)
│   │   ├── include/                  # Header declarations (.h)
│   │   │   ├── memory.h
│   │   │   ├── uds_driver.h
│   │   │   └── doip_server.h
│   │   ├── src/                      # Source implementation (.c)
│   │   │   ├── main.c
│   │   │   ├── memory.c
│   │   │   ├── uds_driver.c
│   │   │   ├── doip_server.c
│   │   │   └── tclsh_wrapper.c
│   │   └── build/                    # Output binaries directory
│   │       └── vECU.exe              # Compiled vECU executable
│   │
│   └── test-runner/                  # App 2: C# .NET WPF Desktop Client & Backend
│       ├── AutVecu.Cores/            # Core DoIP socket transport library
│       ├── AutVecu.Services/         # Test script generator & execution service
│       ├── AutVecu.Desktop/          # WPF Desktop UI Dashboard application
│       ├── AutVecu.CliRunner/        # Command-line test runner
│       └── AUT_vECU.sln              # Visual Studio Solution file
│
├── simulink/                         # MATLAB / Simulink Powertrain Model
│   ├── UWAFT_Blazer_P4_4WD_Opt.slx   # Closed-loop engine thermal dynamics model
│   ├── send_to_vecu.m                # 10 Hz DoIP co-simulation bridge script
│   └── README_SIMULINK.md            # Simulink setup & execution guide
│
├── test-scripts/                     # Automated Test Suites & Configurations
│   ├── lib/                          # Helper Tcl libraries
│   │   ├── socket_client.tcl         # Tcl socket client helper library
│   │   ├── test_common.tcl
│   │   └── uds_definitions.tcl
│   ├── config/
│   │   └── test_env.json             # Environment configuration file
│   ├── simulink_json_regression/     # JSON & Tcl Regression Suites
│   │   ├── test_cases.json           # JSON test sequence specifications
│   │   └── generated/vtc_cases/      # Vehicle Test Cases (VTC)
│   └── logs/                         # Automated test result logs
│       └── .gitkeep                  # Log folder placeholder
│
├── ci/                               # Jenkins CI/CD Automation
│   ├── Jenkinsfile                   # Declarative 6-stage Jenkins pipeline
│   └── scripts/                      # Process automation scripts
│       ├── start_vecu.bat            # Launches vECU background process
│       ├── run_tests.bat             # Executes Tcl regression suite
│       └── stop_vecu.bat             # Terminates vECU process & releases socket
│
├── AUTOMOTIVE_SIL_TESTING_DEVOPS_AUTOMATION.pdf  <-- Official Technical White Paper Report
├── .gitignore                        # Git ignore rules (100% ignores docs/, tracks root PDF)
└── README.md                         # Project documentation
```

---

## 🚀 Quick Start Guide

### 1. Build and Run C vECU Core
#### Using GCC (Command Line):
```bash
gcc -Wall -Iapps/vecu/include apps/vecu/src/doip_server.c apps/vecu/src/memory.c apps/vecu/src/uds_driver.c apps/vecu/src/main.c -o apps/vecu/build/vECU.exe -lws2_32
```
#### Execute vECU Server:
```bash
apps/vecu/build/vECU.exe
```
*Console log will confirm:* `-> [DoIP] DoIP Server listening on port 13400...`

#### Using VS Code:
Press `Ctrl + Shift + B` to trigger the pre-configured VS Code build task (`.vscode/tasks.json`).

---

### 2. Launch C# WPF Desktop Control Panel
1. Ensure `vECU.exe` is running in the background.
2. Open `apps/test-runner/AUT_vECU.sln` in Visual Studio 2022.
3. Build and Run the **AutVecu.Desktop** project.
4. Click **Connect** (`127.0.0.1:13400`), perform **Security Access (0x27)**, and click **Read Active DTCs**.

---

### 3. Run MATLAB / Simulink Closed-Loop Co-Simulation
1. Open MATLAB and navigate to `simulink/`.
2. Open `UWAFT_Blazer_P4_4WD_Opt.slx`.
3. Start simulation. The `send_to_vecu.m` block streams speed/RPM/temperature at 10 Hz to the vECU over DoIP and evaluates FiM Limp-Home throttle restrictions.

---

### 4. Run Jenkins CI/CD Automated Regression
1. Set up a Jenkins job pointing to `ci/Jenkinsfile`.
2. Trigger **Build Now**. The 6-stage pipeline automatically compiles C code, launches `vECU.exe`, executes Tcl test cases, releases socket port 13400, and archives test logs.

---

## 🔮 Next-Gen Version 2.0 Roadmap

1. **UDS Flash Programming & DoIP over TLS (ISO 13400-3)**: Implement UDS Flashing services (`0x34 RequestDownload`, `0x36 TransferData`, `0x37 RequestExit`), cryptographic firmware signature verification, and TLS 1.3 encryption.
2. **Multi-vECU Zonal Gateway Architecture**: Simulate a DoIP Central Gateway routing diagnostic traffic to multi-vECUs (Engine, ABS Brake, Body Zonal Controllers).
3. **AI-Powered Test Case Generation**: Automatically parse diagnostic description files (ODX/CDD) to generate 100+ boundary test scenarios.
4. **SIL-to-HIL Microcontroller Migration**: Deploy native C-code modules onto physical target microcontrollers (STMicroelectronics STM32F4 / NXP S32K) for HIL bench validation.

---

## 📄 License & Citation

This independent collaborative research project is published under standard open engineering documentation guidelines.
Refer to **[AUTOMOTIVE_SIL_TESTING_DEVOPS_AUTOMATION.pdf](AUTOMOTIVE_SIL_TESTING_DEVOPS_AUTOMATION.pdf)** for full academic and technical citation details.
