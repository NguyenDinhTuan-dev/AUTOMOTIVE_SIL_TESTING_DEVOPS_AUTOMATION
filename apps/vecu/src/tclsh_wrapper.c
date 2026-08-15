#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <windows.h>

void clean_bom(const char* filepath) {
    FILE* file = fopen(filepath, "rb");
    if (!file) {
        return;
    }

    // Check if the file starts with UTF-8 BOM: 0xEF, 0xBB, 0xBF
    unsigned char bom[3];
    size_t read_bytes = fread(bom, 1, 3, file);
    if (read_bytes == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) {
        fseek(file, 0, SEEK_END);
        long file_size = ftell(file);
        long data_size = file_size - 3;
        
        if (data_size > 0) {
            fseek(file, 3, SEEK_SET); // skip BOM
            unsigned char* buffer = (unsigned char*)malloc(data_size);
            if (buffer) {
                size_t actually_read = fread(buffer, 1, data_size, file);
                fclose(file);

                // Rewrite the file without BOM
                file = fopen(filepath, "wb");
                if (file) {
                    fwrite(buffer, 1, actually_read, file);
                    fclose(file);
                }
                free(buffer);
            } else {
                fclose(file);
            }
        } else {
            // File only contains BOM, just truncate it
            fclose(file);
            file = fopen(filepath, "wb");
            if (file) {
                fclose(file);
            }
        }
    } else {
        fclose(file);
    }
}

int main(int argc, char* argv[]) {
    // 1. Check if any argument is a .tcl file, and clean BOM if so.
    for (int i = 1; i < argc; i++) {
        char* ext = strrchr(argv[i], '.');
        if (ext && (_stricmp(ext, ".tcl") == 0)) {
            clean_bom(argv[i]);
        }
    }

    // 2. Build the command line to call tclsh_real.exe or tclsh90.exe.
    // Get path of current executable to locate real tclsh in the same folder.
    char exePath[MAX_PATH];
    GetModuleFileNameA(NULL, exePath, MAX_PATH);
    char* lastSlash = strrchr(exePath, '\\');
    if (lastSlash) {
        *lastSlash = '\0';
    }
    
    char realTclshPath[MAX_PATH];
    sprintf(realTclshPath, "%s\\tclsh_real.exe", exePath);

    // If tclsh_real.exe doesn't exist, fallback to tclsh90.exe in the same folder
    DWORD attrib = GetFileAttributesA(realTclshPath);
    if (attrib == INVALID_FILE_ATTRIBUTES || (attrib & FILE_ATTRIBUTE_DIRECTORY)) {
        sprintf(realTclshPath, "%s\\tclsh90.exe", exePath);
    }

    // Construct the argument string for CreateProcess.
    // CreateProcess needs the full command line including the executable name.
    char cmdLine[32768] = {0};
    strcat(cmdLine, "\"");
    strcat(cmdLine, realTclshPath);
    strcat(cmdLine, "\"");

    for (int i = 1; i < argc; i++) {
        strcat(cmdLine, " \"");
        strcat(cmdLine, argv[i]);
        strcat(cmdLine, "\"");
    }

    // 3. Start real tclsh, forwarding stdin/stdout/stderr handles.
    STARTUPINFOA si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    ZeroMemory(&pi, sizeof(pi));

    if (!CreateProcessA(
            NULL,           // No module name
            cmdLine,        // Command line
            NULL,           // Process handle not inheritable
            NULL,           // Thread handle not inheritable
            TRUE,           // Inherit handles (important for output redirection)
            0,              // No creation flags
            NULL,           // Use parent's environment block
            NULL,           // Use parent's starting directory 
            &si,            // Pointer to STARTUPINFO
            &pi             // Pointer to PROCESS_INFORMATION
        )) 
    {
        printf("Failed to execute real tclsh: %d\n", GetLastError());
        return 1;
    }

    // Wait until child process exits
    WaitForSingleObject(pi.hProcess, INFINITE);

    // Get the exit code
    DWORD exitCode = 0;
    GetExitCodeProcess(pi.hProcess, &exitCode);

    // Close process and thread handles
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);

    return exitCode;
}
