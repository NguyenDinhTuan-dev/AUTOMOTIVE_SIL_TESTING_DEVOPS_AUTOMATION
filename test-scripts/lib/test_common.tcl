proc log_info {message} {
    puts "INFO $message"
}

proc log_pass {message} {
    puts "PASS $message"
}

proc log_fail {message} {
    puts "FAIL $message"
}

proc log_error {message} {
    puts "ERROR $message"
}

proc assert_prefix {actual expectedPrefix passMessage failMessage} {
    if {[string match "$expectedPrefix*" $actual]} {
        log_pass $passMessage
        return 0
    }

    log_fail "$failMessage Actual=$actual ExpectedPrefix=$expectedPrefix"
    return 1
}
