# TCC Compatibility Guide for Kogna UART Passthrough

## Overview

This guide explains the TCC (Tiny C Compiler) compatibility issues and solutions for the Kogna UART passthrough program. TCC is commonly used for Kogna development due to its small size and fast compilation, but it has specific quirks that need to be addressed.

## TCC Quirks and Solutions

### 1. Variable Declarations

**Issue**: TCC requires all variables to be declared at the beginning of blocks, not mixed with code.

**Original (GCC-compatible)**:
```c
for (int i = 0; i < length; i++) {
    RS422_PutChar(data[i]);
}
```

**TCC-compatible**:
```c
int i;
for (i = 0; i < length; i++) {
    RS422_PutChar(data[i]);
}
```

### 2. C99 Features

**Issue**: TCC has limited support for C99 features like variable declarations in for loops and compound literals.

**Solutions**:
- Declare all variables at block start
- Avoid compound literals
- Use explicit type casting
- Avoid designated initializers

### 3. Header Files

**Issue**: TCC may have issues with some system headers.

**Solution**: Only include necessary headers:
```c
#include "KMotionDef.h"  // Only KMotion header needed
```

### 4. Function Declarations

**Issue**: TCC is more strict about function declarations.

**Solution**: Declare all functions before use:
```c
/* Function prototypes */
unsigned short ModbusCRC16(const unsigned char* buf, int len);
void send_rs485_modbus(int slave_id, int register_addr, int value, int is_write);
void send_rs232_command(const unsigned char* data, int length);
void configure_uart(int port_type, int baud_rate);
```

### 5. Type Casting

**Issue**: TCC may be more strict about type casting.

**Solution**: Use explicit casting:
```c
rs232_data[i] = (unsigned char)(*(float *)&persist.UserData[6 + i]);
```

## Key Changes Made for TCC Compatibility

### 1. Variable Declaration Style

**Before**:
```c
void send_rs485_modbus(int slave_id, int register_addr, int value, int is_write)
{
    printf("RS485 Modbus: Slave=%d, Reg=0x%04X, Val=%d, Write=%d\n", 
           slave_id, register_addr, value, is_write);

    // Configure RS485 if not already done
    static int rs485_configured = 0;
    if (!rs485_configured) {
        EnableRS422Cmds(RS485_BAUD);
        DoRS422Cmds = FALSE;
        RS422_SetBaudRate(RS485_BAUD, 8, FALSE, FALSE, TRUE);
        rs485_configured = 1;
    }

    // Inter-frame gap (required by Modbus RTU spec)
    Delay_sec(0.01);

    // Build Modbus RTU frame
    unsigned char tx[8];
    int function_code = is_write ? 0x06 : 0x03;
    // ... rest of function
}
```

**After (TCC-compatible)**:
```c
void send_rs485_modbus(int slave_id, int register_addr, int value, int is_write)
{
    unsigned char tx[8];
    int function_code;
    unsigned short crc;
    int i;
    unsigned char rx[32];
    int rxlen;
    double t0;
    int bytes;
    unsigned char* frame;
    int framelen;
    unsigned short crc_reply;
    int reg_value;
    int j;
    static int rs485_configured = 0;
    
    printf("RS485 Modbus: Slave=%d, Reg=0x%04X, Val=%d, Write=%d\n", 
           slave_id, register_addr, value, is_write);

    /* Configure RS485 if not already done */
    if (rs485_configured == 0) {
        EnableRS422Cmds(RS485_BAUD);
        DoRS422Cmds = FALSE;
        RS422_SetBaudRate(RS485_BAUD, 8, FALSE, FALSE, TRUE);
        rs485_configured = 1;
    }

    /* Inter-frame gap (required by Modbus RTU spec) */
    Delay_sec(0.01);

    /* Build Modbus RTU frame */
    function_code = is_write ? 0x06 : 0x03;
    // ... rest of function
}
```

### 2. Loop Variable Declarations

**Before**:
```c
for (int i = 0; i < 8; i++) {
    RS422_PutChar(tx[i]);
    printf("%02X ", tx[i]);
}
```

**After**:
```c
int i;
for (i = 0; i < 8; i++) {
    RS422_PutChar(tx[i]);
    printf("%02X ", tx[i]);
}
```

### 3. Switch Statement to If-Else

**Before**:
```c
switch (command) {
    case 1: // RS485 Modbus communication
        if (slave < 1 || slave > 247) slave = DEFAULT_SLAVE;
        send_rs485_modbus(slave, register_addr, value, (value != 0));
        break;
    case 2: // RS232 communication
        // ... RS232 code
        break;
    case 3: // UART configuration
        configure_uart(port, value);
        break;
    default:
        printf("Unknown command: %d\n", command);
        break;
}
```

**After**:
```c
if (command == 1) { /* RS485 Modbus communication */
    if (slave < 1 || slave > 247) {
        slave = DEFAULT_SLAVE;
    }
    send_rs485_modbus(slave, register_addr, value, (value != 0));
} else if (command == 2) { /* RS232 communication */
    /* ... RS232 code */
} else if (command == 3) { /* UART configuration */
    configure_uart(port, value);
} else {
    printf("Unknown command: %d\n", command);
}
```

### 4. Comment Style

**Before**: C++ style comments
```c
// This is a C++ style comment
```

**After**: C style comments
```c
/* This is a C style comment */
```

## Compilation Commands

### Basic TCC Compilation
```bash
tcc -Wall -O2 -o kogna_uart_passthrough.exe kogna_uart_passthrough_tcc.c -lKMotion
```

### With Debug Information
```bash
tcc -Wall -g -o kogna_uart_passthrough.exe kogna_uart_passthrough_tcc.c -lKMotion
```

### Using Makefile
```bash
make -f Makefile_tcc
```

## Common TCC Compilation Errors and Solutions

### 1. "Variable declaration not allowed here"
**Error**: TCC doesn't allow variable declarations in the middle of blocks.

**Solution**: Move all variable declarations to the beginning of the function.

### 2. "Expected ';' before '{'"
**Error**: TCC is strict about semicolons and braces.

**Solution**: Ensure proper semicolon placement and brace matching.

### 3. "Undefined reference to 'KMotion' functions"
**Error**: Missing KMotion library linkage.

**Solution**: Add `-lKMotion` to the compilation command.

### 4. "Cannot open include file"
**Error**: TCC can't find header files.

**Solution**: Ensure KMotionDef.h is in the include path or current directory.

## Testing TCC Compatibility

### 1. Syntax Check
```bash
tcc -fsyntax-only kogna_uart_passthrough_tcc.c
```

### 2. Compilation Test
```bash
tcc -Wall -o test.exe kogna_uart_passthrough_tcc.c -lKMotion
```

### 3. Size Check
```bash
dir kogna_uart_passthrough.exe
```

## Deployment with TCC

### 1. Using the Batch Script
```cmd
deploy_to_kogna.bat
```

### 2. Manual Compilation
```cmd
tcc -Wall -O2 -o kogna_uart_passthrough.exe kogna_uart_passthrough_tcc.c -lKMotion
```

### 3. Using Makefile
```cmd
make -f Makefile_tcc
```

## Performance Considerations

### TCC Advantages
- Fast compilation
- Small executable size
- Good for embedded systems
- Compatible with Kogna environment

### TCC Limitations
- Limited C99 support
- Stricter syntax requirements
- Fewer optimization options
- May not support all GCC extensions

## Troubleshooting

### 1. Compilation Fails
- Check variable declarations are at block start
- Ensure all functions are declared before use
- Verify KMotion library is available
- Check for C99 features that TCC doesn't support

### 2. Runtime Errors
- Verify KMotionDef.h is correct for your Kogna version
- Check that all KMotion functions are available
- Ensure proper parameter passing via persist buffer

### 3. Communication Issues
- Verify UART configuration
- Check hardware connections
- Test with simple commands first

## Best Practices for TCC Development

1. **Declare all variables at function start**
2. **Use C-style comments (/* */)**
3. **Avoid C99 features**
4. **Test compilation frequently**
5. **Keep functions simple and focused**
6. **Use explicit type casting**
7. **Avoid complex expressions**

## Conclusion

The TCC-compatible version (`kogna_uart_passthrough_tcc.c`) addresses all major TCC quirks while maintaining full functionality. The program follows TCC's stricter syntax requirements while providing the same UART passthrough capabilities as the GCC version.

Key benefits of the TCC version:
- Faster compilation
- Smaller executable size
- Better compatibility with Kogna environment
- Easier deployment and testing 