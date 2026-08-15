namespace eval uds {
    variable serviceId
    array set serviceId {
        diagnosticSessionControl 0x10
        ecuReset 0x11
        clearDiagnosticInformation 0x14
        readDtcInformation 0x19
        readDataByIdentifier 0x22
        securityAccess 0x27
        writeDataByIdentifier 0x2E
        routineControl 0x31
        testerPresent 0x3E
    }

    variable dataIdentifier
    array set dataIdentifier {
        vin 0xF190
        overheatFault 0x0105
    }

    variable readDtcReportType
    array set readDtcReportType {
        reportDtcByStatusMask 0x02
        reportSupportedDtc 0x0A
    }

    variable dataValue
    array set dataValue {
        overheatFaultActive 0x78
    }
}

proc uds::hex_byte {value} {
    return [format "%02X" [expr {$value & 0xFF}]]
}

proc uds::word_bytes {value} {
    return [list [hex_byte [expr {$value >> 8}]] [hex_byte $value]]
}

proc uds::join_bytes {bytes} {
    return [join $bytes " "]
}

proc uds::positive_service_id {serviceName} {
    variable serviceId
    return [hex_byte [expr {$serviceId($serviceName) + 0x40}]]
}

proc uds::request {serviceName args} {
    variable serviceId
    return [join_bytes [concat [list [hex_byte $serviceId($serviceName)]] $args]]
}

proc uds::positive_response_prefix {serviceName args} {
    return [join_bytes [concat [list [positive_service_id $serviceName]] $args]]
}

proc uds::read_data_by_identifier_request {didName} {
    variable dataIdentifier
    return [request readDataByIdentifier {*}[word_bytes $dataIdentifier($didName)]]
}

proc uds::read_data_by_identifier_positive_prefix {didName} {
    variable dataIdentifier
    return [positive_response_prefix readDataByIdentifier {*}[word_bytes $dataIdentifier($didName)]]
}

proc uds::read_dtc_by_status_mask_request {{statusMask 0xFF}} {
    variable readDtcReportType
    return [request readDtcInformation [hex_byte $readDtcReportType(reportDtcByStatusMask)] [hex_byte $statusMask]]
}

proc uds::read_dtc_by_status_mask_positive_prefix {} {
    variable readDtcReportType
    return [positive_response_prefix readDtcInformation [hex_byte $readDtcReportType(reportDtcByStatusMask)]]
}

proc uds::write_data_by_identifier_request {didName dataValueName} {
    variable dataIdentifier
    variable dataValue
    return [request writeDataByIdentifier {*}[word_bytes $dataIdentifier($didName)] [hex_byte $dataValue($dataValueName)]]
}

proc uds::write_data_by_identifier_positive_prefix {didName} {
    variable dataIdentifier
    return [positive_response_prefix writeDataByIdentifier {*}[word_bytes $dataIdentifier($didName)]]
}