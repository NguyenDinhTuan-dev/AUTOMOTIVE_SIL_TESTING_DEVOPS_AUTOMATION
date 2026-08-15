source [file normalize "D:/AUT_vECU/test-scripts/lib/socket_client.tcl"]

log_info "Start SIM_READ_RPM_ReadRpmFromSimulink"

log_info "Enter extended diagnostic session"
set step1Request "10 03"
set step1Response [send_uds_request $step1Request]
assert_prefix $step1Response "50 03" "Enter extended diagnostic session prefix matched." "Enter extended diagnostic session failed."

log_info "Require live Simulink update"
set step2Request "22 01 FF"
set step2FirstResponse [send_uds_request $step2Request]
if {![string match "62 01 FF*" $step2FirstResponse]} {
    log_fail "Require live Simulink update failed. Actual=$step2FirstResponse ExpectedPrefix=62 01 FF"
    exit 1
}
set step2FirstClean [string toupper [string map {" " "" "\t" "" "\r" "" "\n" ""} $step2FirstResponse]]
set step2Changed 0
set step2Deadline [expr {[clock milliseconds] + 1500}]
while {[clock milliseconds] < $step2Deadline} {
    after 250
    set step2NextResponse [send_uds_request $step2Request]
    if {![string match "62 01 FF*" $step2NextResponse]} {
        log_fail "Require live Simulink update failed. Actual=$step2NextResponse ExpectedPrefix=62 01 FF"
        exit 1
    }
    set step2NextClean [string toupper [string map {" " "" "\t" "" "\r" "" "\n" ""} $step2NextResponse]]
    if {$step2NextClean ne $step2FirstClean} {
        set step2Changed 1
        log_pass "Require live Simulink update detected live Simulink data update."
        break
    }
}
if {!$step2Changed} {
    log_fail "Require live Simulink update failed. Simulink is not updating vECU data. First=$step2FirstResponse"
    exit 1
}

log_info "Read engine RPM"
set step3Request "22 01 01"
set step3Response [send_uds_request $step3Request]
assert_prefix $step3Response "62 01 01" "Read engine RPM prefix matched." "Read engine RPM failed."
set step3ActualClean [string toupper [string map {" " "" "\t" "" "\r" "" "\n" ""} $step3Response]]
set step3NotEqualsClean [string toupper [string map {" " "" "\t" "" "\r" "" "\n" ""} "62 01 01 03 20"]]
if {$step3ActualClean ne $step3NotEqualsClean} {
    log_pass "Read engine RPM is not default Simulink-off value."
} else {
    log_fail "Read engine RPM failed. Actual=$step3Response must not equal 62 01 01 03 20. Start Simulink before running this test."
    exit 1
}

