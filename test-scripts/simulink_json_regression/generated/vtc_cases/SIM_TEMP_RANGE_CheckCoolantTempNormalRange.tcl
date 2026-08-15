source [file normalize "D:/AUT_vECU/test-scripts/lib/socket_client.tcl"]

log_info "Start SIM_TEMP_RANGE_CheckCoolantTempNormalRange"

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

log_info "Check coolant temperature range"
set step3Request "22 01 02"
set step3Response [send_uds_request $step3Request]
assert_prefix $step3Response "62 01 02" "Check coolant temperature range prefix matched." "Check coolant temperature range failed."
set step3RangeClean [string toupper [string map {" " "" "\t" "" "\r" "" "\n" ""} $step3Response]]
set step3RangeStart 6
if {[string length $step3RangeClean] < [expr {$step3RangeStart + 2}]} {
    log_fail "Check coolant temperature range failed. Response too short for byte index 3. Actual=$step3Response"
    exit 1
}
set step3RangeHex [string range $step3RangeClean $step3RangeStart [expr {$step3RangeStart + 1}]]
scan $step3RangeHex %x step3RangeValue
if {$step3RangeValue >= 78 && $step3RangeValue <= 99} {
    log_pass "Check coolant temperature range value $step3RangeValue is in range 78..99."
} else {
    log_fail "Check coolant temperature range failed. Value=$step3RangeValue ExpectedRange=78..99 Actual=$step3Response"
    exit 1
}

