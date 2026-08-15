function dtc_active = send_to_vecu(speed, rpm, temp)
    persistent client;
    persistent error_counter; % Diagnostic noise filter counter (Debounce)
    persistent last_run_time; % Cycle elapsed time tracker for pause detection
    
    current_time = clock;
    
    if isempty(error_counter)
        error_counter = 0;
    end
    
    dtc_active = 0; % Default initial status
    
    % Check if new DoIP socket connection is required
    need_connect = false;
    if isempty(client)
        need_connect = true;
    else
        % Detect simulation pause >= 4.0s to handle vECU S3 Server Timeout
        % Re-initiate DoIP routing activation and Security Access handshake upon resume
        if isempty(last_run_time)
            last_run_time = current_time;
        end
        if etime(current_time, last_run_time) > 4
            need_connect = true;
        end
    end
    last_run_time = current_time;
    
    if need_connect
        try
            if ~isempty(client)
                try delete(client); catch; end
            end
            client = tcpclient('127.0.0.1', 13400, 'Timeout', 0.2);
            
            % 1. Send DoIP Routing Activation Request (0x0005)
            % Tester address = 0x0E00 (14, 0), Target = 0x0E80 (14, 128)
            packet_act = uint8([2, 253, 0, 5, 0, 0, 0, 7, 14, 0, 0, 0, 0, 0, 0]);
            write(client, packet_act);
            
            % Await DoIP Routing Activation Response (0x0006)
            t_cnt = 0;
            while client.NumBytesAvailable < 21 && t_cnt < 20
                pause(0.005);
                t_cnt = t_cnt + 1;
            end
            
            if client.NumBytesAvailable >= 21
                read(client, 21);
            else
                if client.NumBytesAvailable > 0
                    read(client, client.NumBytesAvailable);
                end
            end
            
            % 1b. Send UDS DiagnosticSessionControl request for Extended Session (10 03)
            packet_session_req = uint8([2, 253, 128, 1, 0, 0, 0, 6, 14, 0, 14, 128, 16, 3]);
            write(client, packet_session_req);
            
            % Await DiagnosticSessionControl positive response (50 03)
            t_cnt = 0;
            while client.NumBytesAvailable < 27 && t_cnt < 20
                pause(0.005);
                t_cnt = t_cnt + 1;
            end
            if client.NumBytesAvailable > 0
                read(client, client.NumBytesAvailable); % Flush receive socket buffer
            end
            
            % 2. Send UDS SecurityAccess Request Seed (27 01)
            % DoIP 0x8001, payload length = 6 (src 14 0, dst 14 128, uds 27 01)
            packet_seed_req = uint8([2, 253, 128, 1, 0, 0, 0, 6, 14, 0, 14, 128, 39, 1]);
            write(client, packet_seed_req);
            
            % Await SecurityAccess Request Seed response (67 01)
            t_cnt = 0;
            while client.NumBytesAvailable < 29 && t_cnt < 20
                pause(0.005);
                t_cnt = t_cnt + 1;
            end
            
            resp = [];
            if client.NumBytesAvailable >= 29
                resp = read(client, client.NumBytesAvailable);
            end
            
            if length(resp) >= 29
                seed_high = double(resp(28));
                seed_low = double(resp(29));
                seed = seed_high * 256 + seed_low;
                
                % Calculate Security Access Key: Key = (Seed ^ 0x5A5A) + 0x1234
                key = bitand(bitxor(seed, 23130) + 4660, 65535);
                key_high = uint8(bitand(bitshift(key, -8), 255));
                key_low = uint8(bitand(key, 255));
                
                % 3. Send UDS SecurityAccess Send Key (27 02 [Key])
                packet_key_send = uint8([2, 253, 128, 1, 0, 0, 0, 8, 14, 0, 14, 128, 39, 2, key_high, key_low]);
                write(client, packet_key_send);
                
                % Await SecurityAccess Send Key positive response (67 02)
                t_cnt = 0;
                while client.NumBytesAvailable < 27 && t_cnt < 20
                    pause(0.005);
                    t_cnt = t_cnt + 1;
                end
                if client.NumBytesAvailable > 0
                    read(client, client.NumBytesAvailable);
                end
            end
        catch
            client = [];
            return;
        end
    end

    % Stream periodic signals over unlocked DoIP diagnostic channel
    try
        % 1. Send Coolant Temp (DID 0x0102 - 1 byte payload)
        temp_byte = uint8(temp);
        packet_temp = uint8([2, 253, 128, 1, 0, 0, 0, 8, 14, 0, 14, 128, 46, 1, 2, temp_byte]);
        write(client, packet_temp);

        % 2. Send Vehicle Speed (DID 0x0100 - 2 bytes payload)
        speed_uint = uint16(speed);
        speed_high = uint8(bitand(bitshift(speed_uint, -8), 255));
        speed_low = uint8(bitand(speed_uint, 255));
        packet_speed = uint8([2, 253, 128, 1, 0, 0, 0, 9, 14, 0, 14, 128, 46, 1, 0, speed_high, speed_low]);
        write(client, packet_speed);

        % 3. Send Engine RPM (DID 0x0101 - 2 bytes payload)
        rpm_uint = uint16(rpm);
        rpm_high = uint8(bitand(bitshift(rpm_uint, -8), 255));
        rpm_low = uint8(bitand(rpm_uint, 255));
        packet_rpm = uint8([2, 253, 128, 1, 0, 0, 0, 9, 14, 0, 14, 128, 46, 1, 1, rpm_high, rpm_low]);
        write(client, packet_rpm);

        % Flush receive buffer after signal write commands
        % Prepare socket buffer for clean DTC response parsing
        t_flush = 0;
        while client.NumBytesAvailable > 0 && t_flush < 20
            read(client, client.NumBytesAvailable);
            pause(0.001);
            t_flush = t_flush + 1;
        end

        % 4. Query active DTCs (19 02 FF - Read DTC by status mask 0xFF)
        packet_dtc = uint8([2, 253, 128, 1, 0, 0, 0, 7, 14, 0, 14, 128, 25, 2, 255]);
        write(client, packet_dtc);

        % Await ReadDTCInformation response frame
        t_cnt = 0;
        % Minimum DTC response length is 15 bytes (DoIP Header + UDS Payload)
        while client.NumBytesAvailable < 15 && t_cnt < 16
            pause(0.005);
            t_cnt = t_cnt + 1;
        end
        
        if client.NumBytesAvailable > 0
            resp_dtc = read(client, client.NumBytesAvailable);
            % Check if the response contains active DTC P0115 ([0, 1, 21]) or P0217 ([0, 2, 23])
            if ~isempty(strfind(double(resp_dtc), [0, 1, 21])) || ~isempty(strfind(double(resp_dtc), [0, 2, 23]))
                error_counter = 10; % Hold active fault state for 10 cycles (~1.0s debounce)
            end
        end
    catch
        % Handle socket disconnection exceptions
        try delete(client); catch; end
        client = [];
    end
    
    % Apply debounce filter hysteresis
    if error_counter > 0
        dtc_active = 1;
        error_counter = error_counter - 1;
    else
        dtc_active = 0;
    end
end
