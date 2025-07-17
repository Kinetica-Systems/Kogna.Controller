# RS485 Passthrough Fix Summary

## Problem Identified

The RS485 passthrough functionality was not working in the TCC-compiled version of the C program. After comparing with the working `RS485_Push_test.c` program, several critical differences were identified:

## Key Issues Found

### 1. Parameter Mapping Mismatch
**Problem**: The C# server was setting persist data with wrong indices
- **C# was using**: `UserData[0]=1`, `UserData[2]=slave`, `UserData[3]=register`, `UserData[4]=value`
- **Working C program expects**: `UserData[0]=slave`, `UserData[1]=register`, `UserData[2]=value`

### 2. Modbus Frame Construction
**Problem**: The TCC version was building Modbus frames dynamically, but the working version uses a hardcoded frame
- **TCC version**: Built frame based on parameters (slave_id, register_addr, value)
- **Working version**: Uses hardcoded frame `{0x01, 0x03, 0xFD, 0x00, 0x00, 0x01, 0x00, 0x00}`

### 3. Response Validation Logic
**Problem**: Different validation logic for Modbus responses
- **TCC version**: Looked for `rx[i] == slave_id && (rx[i+1] == 0x03 || rx[i+1] == 0x06)`
- **Working version**: Looks for `rx[i] == 0x01 && rx[i+1] == 0x03`

### 4. Dynamic Register Addressing
**Problem**: The hardcoded frame approach only worked for one specific register (0x30FD)
- **Issue**: When the app sends commands for different registers (0x3001, 0x3002, etc.), the hardcoded frame was still reading 0x30FD
- **Solution**: Reverted to dynamic frame building but with proper parameter handling

## Fixes Applied

### 1. Fixed Parameter Mapping in C# Server
```csharp
// OLD (incorrect):
_io.WriteLineReadLine(1, "SetPersist UserData[0] 1", out _);
_io.WriteLineReadLine(1, $"SetPersist UserData[2] {addr}", out _);
_io.WriteLineReadLine(1, $"SetPersist UserData[3] {regAddr}", out _);
_io.WriteLineReadLine(1, $"SetPersist UserData[4] {regValue}", out _);

// NEW (correct):
_io.WriteLineReadLine(1, $"SetPersist UserData[0] {addr}", out _);
_io.WriteLineReadLine(1, $"SetPersist UserData[1] {regAddr}", out _);
_io.WriteLineReadLine(1, $"SetPersist UserData[2] {regValue}", out _);
```

### 2. Updated C Program Parameter Handling
```c
// OLD (complex parameter mapping):
f_command = *(float *)&persist.UserData[0];
f_port = *(float *)&persist.UserData[1];
f_slave = *(float *)&persist.UserData[2];
f_register = *(float *)&persist.UserData[3];
f_value = *(float *)&persist.UserData[4];

// NEW (simple, matches working version):
f_slave = *(float *)&persist.UserData[0];
f_register = *(float *)&persist.UserData[1];
f_value = *(float *)&persist.UserData[2];
```

### 3. Dynamic Modbus Frame Building
```c
// OLD (hardcoded frame):
tx[0] = 0x01;  /* Slave ID */
tx[1] = 0x03;  /* Function code: Read Holding Registers */
tx[2] = 0xFD;  /* Register high byte: 0x30FD */
tx[3] = 0x00;  /* Register low byte: 0x00 */

// NEW (dynamic frame building):
tx[0] = slave_id;
tx[1] = function_code;
tx[2] = (register_addr >> 8) & 0xFF;  /* Register high byte */
tx[3] = register_addr & 0xFF;          /* Register low byte */
```

### 4. Fixed Response Validation
```c
// OLD (simple validation):
if (rx[i] == 0x01 && rx[i+1] == 0x03)

// NEW (dynamic validation):
if (rx[i] == slave_id && (rx[i+1] == 0x03 || rx[i+1] == 0x06))
```

### 5. Added Write Command Support
```c
// NEW: Support for both read and write commands
if (rx[i+1] == 0x03 && bytes == 2) {
    // Handle read response
    reg_value = (frame[3] << 8) | frame[4];
    persist.UserData[10] = reg_value;
} else if (rx[i+1] == 0x06) {
    // Handle write confirmation
    persist.UserData[10] = 1;
}
```

## Files Modified

1. **`kogna_uart_passthrough_tcc.c`**
   - Updated parameter mapping in `main()` function
   - Implemented dynamic Modbus frame building
   - Fixed response validation logic to handle both read and write
   - Added support for different register addresses

2. **`App/Server/Server.cs`**
   - Fixed persist data indices in RS485 command handler
   - Changed from `UserData[0,2,3,4]` to `UserData[0,1,2]`

3. **`test_rs485_dynamic.py`**
   - New comprehensive test script
   - Tests multiple register addresses
   - Tests both read and write commands
   - Tests servo status commands

## Testing

The fix can be tested using:
```bash
python test_rs485_dynamic.py
```

This will test:
- Multiple register addresses (0x3001, 0x3002, 0x3004, 0x3005, 0x1000, 0x3000)
- Both read and write commands
- Servo status commands for different parameters

## Expected Behavior

After the fix:
1. RS485 commands should successfully communicate with FS50L servo drives
2. The C program should build Modbus frames dynamically based on the requested register
3. Both read and write commands should work correctly
4. Different register addresses should be handled properly
5. Responses should be properly validated and decoded
6. Results should be returned via `persist.UserData[10]`

## Compilation

The updated program compiles successfully with TCC:
```bash
tcc -o kogna_uart_passthrough_tcc.out kogna_uart_passthrough_tcc.c
```

## Deployment

The compiled program should be deployed to the Kogna controller and the C# application should be restarted to use the updated server code.

## Key Improvement

The main improvement is that the C program now builds Modbus frames dynamically based on the actual register address passed from the app, rather than using a hardcoded frame that only worked for one specific register. This allows the system to work with any FS50L register address that the app requests. 