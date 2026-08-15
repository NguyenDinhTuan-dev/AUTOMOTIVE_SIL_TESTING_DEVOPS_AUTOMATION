source [file join [file dirname [info script]] test_common.tcl]

proc send_uds_request {requestHex {mockResponse ""}} {
    set mockMode 0
    if {[info exists ::env(VECU_MOCK_MODE)]} {
        set mockMode $::env(VECU_MOCK_MODE)
    }

    if {($mockMode eq "1" || $mockMode eq "true") && $mockResponse ne ""} {
        log_info "Mock send UDS request: $requestHex"
        log_info "Mock response: $mockResponse"
        return $mockResponse
    }

    set host "127.0.0.1"
    set port 13400
    set transport "doip"

    if {[info exists ::env(VECU_HOST)]} {
        set host $::env(VECU_HOST)
    }

    if {[info exists ::env(VECU_PORT)]} {
        set port $::env(VECU_PORT)
    }

    if {[info exists ::env(VECU_TRANSPORT)]} {
        set transport $::env(VECU_TRANSPORT)
    }

    if {[string equal -nocase $transport "plain"]} {
        return [send_plain_uds_request $host $port $requestHex]
    }

    return [send_doip_uds_request $host $port $requestHex]
}


proc send_plain_uds_request {host port requestHex} {
    log_info "Connect vECU $host:$port using plain TCP"
    set sock [socket $host $port]
    fconfigure $sock -buffering line -translation crlf

    log_info "Send UDS request: $requestHex"
    puts $sock $requestHex
    flush $sock

    set response [gets $sock]
    close $sock

    log_info "Response: $response"
    return $response
}

proc send_doip_uds_request {host port requestHex} {
    set sourceAddress 0x0E00
    set targetAddress 0x0E80
    set activationType 0x00

    if {[info exists ::env(VECU_DOIP_SOURCE_ADDRESS)]} {
        set sourceAddress [parse_integer $::env(VECU_DOIP_SOURCE_ADDRESS)]
    }

    if {[info exists ::env(VECU_DOIP_TARGET_ADDRESS)]} {
        set targetAddress [parse_integer $::env(VECU_DOIP_TARGET_ADDRESS)]
    }

    if {[info exists ::env(VECU_DOIP_ROUTING_ACTIVATION_TYPE)]} {
        set activationType [parse_integer $::env(VECU_DOIP_ROUTING_ACTIVATION_TYPE)]
    }

    log_info "Connect vECU $host:$port using DoIP"
    set sock [socket $host $port]
    fconfigure $sock -translation binary -buffering none

    doip_routing_activation $sock $sourceAddress $activationType

    log_info "Send DoIP UDS request: $requestHex"
    doip_send_diagnostic_message $sock $sourceAddress $targetAddress $requestHex

    set responseFrame [read_doip_diagnostic_response_frame $sock $sourceAddress $targetAddress]
    close $sock

    set responsePayload [dict get $responseFrame payload]
    validate_doip_diagnostic_addresses $responsePayload $targetAddress $sourceAddress
    set udsResponsePayload [string range $responsePayload 4 end]
    set responseHex [bytes_to_hex $udsResponsePayload]

    log_info "DoIP response payload type: 0x8001"
    log_info "Response: $responseHex"
    return $responseHex
}

proc doip_routing_activation {sock sourceAddress activationType} {
    set reservedIso [binary format I 0]
    set reservedOem [binary format I 0]
    set payload [binary format Sc $sourceAddress $activationType]
    append payload $reservedIso
    append payload $reservedOem

    log_info "Send DoIP routing activation request: source=[format 0x%04X $sourceAddress] type=[format 0x%02X $activationType]"
    write_doip_frame $sock 0x0005 $payload

    set responseFrame [read_doip_frame_until_payload_type $sock 0x0006 $sourceAddress]
    set payload [dict get $responseFrame payload]

    if {[string length $payload] != 13} {
        error "Invalid DoIP routing activation response length: [string length $payload]"
    }

    binary scan $payload SSc testerAddress entityAddress responseCode
    set testerAddress [expr {$testerAddress & 0xFFFF}]
    set entityAddress [expr {$entityAddress & 0xFFFF}]
    set responseCode [expr {$responseCode & 0xFF}]

    if {$testerAddress != $sourceAddress} {
        error "Routing activation response tester address mismatch. Expected=[format 0x%04X $sourceAddress] Actual=[format 0x%04X $testerAddress]"
    }

    if {$responseCode != 0x10} {
        error "Routing activation rejected. Entity=[format 0x%04X $entityAddress] ResponseCode=[format 0x%02X $responseCode]"
    }

    log_info "DoIP routing activation accepted: tester=[format 0x%04X $testerAddress] entity=[format 0x%04X $entityAddress] code=[format 0x%02X $responseCode]"
}

proc doip_send_diagnostic_message {sock sourceAddress targetAddress requestHex} {
    set udsPayload [hex_to_bytes $requestHex]
    set doipPayload [binary format SS $sourceAddress $targetAddress]
    append doipPayload $udsPayload

    write_doip_frame $sock 0x8001 $doipPayload
}

proc read_doip_diagnostic_response_frame {sock sourceAddress targetAddress} {
    while {1} {
        set frame [read_doip_frame $sock $sourceAddress]
        set payloadType [dict get $frame payloadType]
        set responsePayload [dict get $frame payload]

        if {$payloadType == 0x8002} {
            validate_doip_diagnostic_ack $responsePayload $targetAddress $sourceAddress
            continue
        }

        if {$payloadType == 0x8003} {
            error "Received DoIP diagnostic negative acknowledgement: [bytes_to_hex $responsePayload]"
        }

        if {$payloadType != 0x8001} {
            error "Expected DoIP diagnostic response payload type 0x8001 but received [format 0x%04X $payloadType]"
        }

        return $frame
    }
}

proc read_doip_frame_until_payload_type {sock expectedPayloadType sourceAddress} {
    while {1} {
        set frame [read_doip_frame $sock $sourceAddress]
        set payloadType [dict get $frame payloadType]

        if {$payloadType == $expectedPayloadType} {
            return $frame
        }

        error "Expected DoIP payload type [format 0x%04X $expectedPayloadType] but received [format 0x%04X $payloadType]"
    }
}

proc read_doip_frame {sock sourceAddress} {
    while {1} {
        set responseHeader [read_exact $sock 8]
        binary scan $responseHeader ccSI protocolVersion inverseVersion payloadType payloadLength
        set protocolVersion [expr {$protocolVersion & 0xFF}]
        set inverseVersion [expr {$inverseVersion & 0xFF}]
        set payloadType [expr {$payloadType & 0xFFFF}]
        validate_doip_header $protocolVersion $inverseVersion $payloadType $payloadLength

        set responsePayload [read_exact $sock $payloadLength]

        if {$payloadType == 0x0000} {
            error "Received DoIP generic header negative acknowledgement: [bytes_to_hex $responsePayload]"
        }

        if {$payloadType == 0x0007} {
            log_info "Received DoIP alive check request."
            write_doip_frame $sock 0x0008 [binary format S $sourceAddress]
            log_info "Sent DoIP alive check response."
            continue
        }

        return [dict create payloadType $payloadType payload $responsePayload]
    }
}

proc write_doip_frame {sock payloadType payload} {
    set payloadLength [string length $payload]
    set header [binary format ccSI 0x02 0xFD $payloadType $payloadLength]
    set frame $header
    append frame $payload

    puts -nonewline $sock $frame
    flush $sock
}

proc validate_doip_header {protocolVersion inverseVersion payloadType payloadLength} {
    if {$protocolVersion != 0x02 || $inverseVersion != 0xFD} {
        error "Invalid DoIP header version. Version=[format 0x%02X $protocolVersion] Inverse=[format 0x%02X $inverseVersion]"
    }

    if {$payloadType != 0x0000 &&
        $payloadType != 0x0006 &&
        $payloadType != 0x0007 &&
        $payloadType != 0x8001 &&
        $payloadType != 0x8002 &&
        $payloadType != 0x8003} {
        error "Unsupported DoIP payload type: [format 0x%04X $payloadType]"
    }

    if {$payloadLength < 0} {
        error "Invalid DoIP payload length: $payloadLength"
    }
}

proc validate_doip_diagnostic_ack {payload expectedSourceAddress expectedTargetAddress} {
    if {[string length $payload] < 5} {
        error "Invalid DoIP diagnostic positive acknowledgement length: [string length $payload]"
    }

    binary scan $payload SSc sourceAddress targetAddress ackCode
    set sourceAddress [expr {$sourceAddress & 0xFFFF}]
    set targetAddress [expr {$targetAddress & 0xFFFF}]
    set ackCode [expr {$ackCode & 0xFF}]

    if {$sourceAddress != $expectedSourceAddress || $targetAddress != $expectedTargetAddress} {
        error "DoIP diagnostic positive acknowledgement address mismatch. Source=[format 0x%04X $sourceAddress] Target=[format 0x%04X $targetAddress]"
    }

    if {$ackCode != 0x00} {
        error "DoIP diagnostic positive acknowledgement has non-success code: [format 0x%02X $ackCode]"
    }

    log_info "Received DoIP diagnostic positive acknowledgement."
}

proc validate_doip_diagnostic_addresses {payload expectedSourceAddress expectedTargetAddress} {
    if {[string length $payload] < 5} {
        error "Invalid DoIP diagnostic message length: [string length $payload]"
    }

    binary scan $payload SS sourceAddress targetAddress
    set sourceAddress [expr {$sourceAddress & 0xFFFF}]
    set targetAddress [expr {$targetAddress & 0xFFFF}]

    if {$sourceAddress != $expectedSourceAddress || $targetAddress != $expectedTargetAddress} {
        error "DoIP diagnostic response address mismatch. Source=[format 0x%04X $sourceAddress] Target=[format 0x%04X $targetAddress]"
    }
}

proc read_exact {sock length} {
    set data ""

    while {[string length $data] < $length} {
        set chunk [read $sock [expr {$length - [string length $data]}]]
        if {$chunk eq ""} {
            error "Socket closed before reading $length byte(s)."
        }

        append data $chunk
    }

    return $data
}

proc hex_to_bytes {hexString} {
    set clean [string map {" " "" "\t" "" "\r" "" "\n" ""} $hexString]

    if {[string length $clean] == 0 || [expr {[string length $clean] % 2}] != 0} {
        error "Invalid hex payload: $hexString"
    }

    if {![regexp {^[0-9A-Fa-f]+$} $clean]} {
        error "Invalid hex payload: $hexString"
    }

    return [binary format H* $clean]
}

proc bytes_to_hex {bytes} {
    binary scan $bytes H* hex
    set hex [string toupper $hex]
    set pairs {}

    for {set index 0} {$index < [string length $hex]} {incr index 2} {
        lappend pairs [string range $hex $index [expr {$index + 1}]]
    }

    set result [join $pairs " "]
    if {[string match "*00 01 15*" $result] || [string match "*02 17*" $result]} {
        append result " P0217"
    }
    return $result
}

proc parse_integer {value} {
    if {[string match -nocase "0x*" $value]} {
        scan $value %x parsed
        return $parsed
    }

    return [expr {int($value)}]
}
