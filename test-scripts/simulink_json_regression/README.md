# Simulink JSON Signal Tests

This folder contains 4 JSON tests:

- Read speed from Simulink/vECU
- Read RPM from Simulink/vECU
- Read coolant temperature from Simulink/vECU
- Check coolant temperature normal range

Each test first sends DiagnosticSessionControl `10 03` to enter the extended diagnostic session and requires a positive response beginning with `50 03`. It then reads DID `0x01FF`, the Simulink update counter. The counter must change within 1500 ms. If it does not change, the test fails because Simulink is not actively updating vECU data.

Run flow:
1. Start vECU.
2. Start Simulink so it writes live speed/RPM/coolant values into vECU.
3. Choose `test-scripts\simulink_json_regression\test_cases.json` or this folder.
4. Click Run Tests.

Expected behavior:
- vECU rejects `10 03` or does not return `50 03`: the test fails before reading Simulink values.
- Simulink running and writing data: tests can pass.
- Simulink not running: tests fail at `Require live Simulink update`.

Temperature tests:
- `SIM_READ_TEMP` only reads coolant temperature and checks response prefix `62 01 02`.
- `SIM_TEMP_RANGE` reads coolant temperature and checks the value range.

Coolant temperature range rule:
- 78 C to 99 C: pass / normal temperature.
- Below 78 C or above 99 C: fail / temperature fault.

These tests enter extended diagnostic session with `0x10`, then only read Simulink-driven values. They do not use `0x27`, do not clear DTC, and do not inject faults.
