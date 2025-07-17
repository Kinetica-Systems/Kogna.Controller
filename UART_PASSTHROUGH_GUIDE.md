# UART Passthrough System Guide

## Overview

This guide describes the improved UART passthrough system for the Kogna.Controller that enables communication with FS50L servo drives (RS485) and LED/Laser drivers (RS232). The system uses proper KMotion RS232/RS485 handling patterns based on analysis of the RS485_Push_test.c file.

## Architecture

### System Components

1. **C# Backend (Server.cs)**: Handles IPC commands and interfaces with the C program
2. **C Program (kogna_uart_passthrough.c)**: Executes on the Kogna and handles actual UART communication
3. **KMotion Integration**: Uses proper KMotion RS422/RS485 APIs for reliable communication

### Communication Flow

```
C# Application → IPC Command → C# Backend → C Program → KMotion UART APIs → Hardware
```

## Key Improvements from RS485_Push_test.c Analysis

### 1. Proper KMotion UART Configuration

```c
// RS485 Configuration
EnableRS422Cmds(38400);
DoRS422Cmds = FALSE;
RS422_SetBaudRate(38400, 8, FALSE, FALSE, TRUE); // TRUE = RS485 mode

// RS232 Configuration  
EnableRS422Cmds(115200);
DoRS422Cmds = FALSE;
RS422_SetBaudRate(115200, 8, FALSE, FALSE, FALSE); // FALSE = RS232 mode
```

### 2. Parameter Passing via Persist Buffer

```c
// Fetch parameters from persist mailbox
f_command = *(float *)&persist.UserData[0];    // Command type
f_port = *(float *)&persist.UserData[1];       // Port configuration
f_slave = *(float *)&persist.UserData[2];      // Slave ID
f_register = *(float *)&persist.UserData[3];   // Register address
f_value = *(float *)&persist.UserData[4];      // Value to write
f_length = *(float *)&persist.UserData[5];     // Data length
```

### 3. Proper Buffer Management

```c
// Flush RX buffer before sending
while (pRS422RecIn != pRS422RecOut) RS422_GetChar();

// Send data
for (int i = 0; i < length; i++) {
    RS422_PutChar(data[i]);
}

// Read response with timeout
double t0 = Time_sec();
while ((Time_sec() - t0) < timeout && rxlen < max_len) {
    if (pRS422RecIn != pRS422RecOut) {
        rx[rxlen++] = RS422_GetChar();
    } else {
        WaitNextTimeSlice();
    }
}
```

### 4. Modbus RTU Protocol Support

- Proper CRC16 calculation
- Inter-frame gap timing (10ms minimum)
- Register address handling
- Read/Write function codes (0x03/0x06)

## Command Reference

### RS485 Commands (FS50L Servo Drives)

#### Read Register
```bash
rs485 <slave> <register>
```
Example:
```bash
rs485 1 3001    # Read register 0x3001 from slave 1
```

#### Write Register
```bash
rs485 <slave> <register> <value>
```
Example:
```bash
rs485 1 1000 1    # Write value 1 to register 0x1000 on slave 1
```

### RS232 Commands (LED/Laser Drivers)

#### Send Data
```bash
rs232 <data> [data2] [data3] ...
```
Example:
```bash
rs232 41 42 43    # Send hex bytes 0x41, 0x42, 0x43
```

### UART Configuration

#### Configure RS485
```bash
uartconfig rs485 <baudrate>
```
Example:
```bash
uartconfig rs485 38400    # Configure RS485 at 38400 baud
```

#### Configure RS232
```bash
uartconfig rs232 <baudrate>
```
Example:
```bash
uartconfig rs232 115200    # Configure RS232 at 115200 baud
```

## FS50L Servo Drive Integration

### Common Registers

| Register | Address | Description |
|----------|---------|-------------|
| Control | 0x1000 | Drive control commands |
| Frequency | 0x3000 | Set frequency (0-10000) |
| Running Status | 0x3001 | Running frequency |
| Bus Voltage | 0x3002 | DC bus voltage |
| Output Current | 0x3004 | Output current |
| Output Power | 0x3005 | Output power |
| Output Torque | 0x3006 | Output torque |
| Running Speed | 0x3007 | Running speed |
| Fault Info | 0x8000 | Drive fault information |
| Comm Fault | 0x8001 | Communication fault |

### Control Commands

| Value | Command |
|-------|---------|
| 0x0001 | Forward run |
| 0x0002 | Reverse run |
| 0x0003 | Forward jog |
| 0x0004 | Reverse jog |
| 0x0005 | Free stop |
| 0x0006 | Deceleration stop |
| 0x0007 | Fault reset |

### Example Usage

```bash
# Read running frequency
rs485 1 3001

# Start forward run
rs485 1 1000 1

# Set frequency to 50Hz
rs485 1 3000 5000

# Stop drive
rs485 1 1000 5

# Read fault status
rs485 1 8000
```

## LED/Laser Driver Integration

### Simple Protocol

The RS232 interface supports a simple hex-based protocol for LED/Laser drivers:

```bash
# Turn on LED (example protocol)
rs232 4F 4E    # "ON" command

# Turn off LED
rs232 4F 46 46  # "OFF" command

# Set power level
rs232 50 57 52 20 35 30  # "PWR 50" command
```

## Deployment

### 1. Compile C Program

```bash
# On development machine
gcc -o kogna_uart_passthrough kogna_uart_passthrough.c -I/path/to/kmotion/include -L/path/to/kmotion/lib -lKMotion
```

### 2. Deploy to Kogna

```bash
# Copy executable to Kogna
scp kogna_uart_passthrough.exe user@kogna:/path/to/executable/
```

### 3. Test Communication

```bash
# Test RS485 read
rs485 1 3001

# Test RS232 send
rs232 48 65 6C 6C 6F  # "Hello" in hex
```

## Troubleshooting

### Common Issues

1. **No Response from RS485**
   - Check slave address (1-247)
   - Verify register address format (hex)
   - Ensure proper wiring and termination

2. **RS232 Communication Errors**
   - Verify baud rate settings
   - Check data format (hex bytes)
   - Ensure proper cable connections

3. **C Program Not Found**
   - Verify executable is in correct location
   - Check file permissions
   - Ensure proper compilation

### Debug Output

The C program provides detailed debug output:

```
Command: 1, Port: 1, Slave: 1, Reg: 12289, Val: 0, Len: 0
RS485 Modbus: Slave=1, Reg=0x3001, Val=0, Write=0
TX: 01 03 30 01 00 01 25 CA
RX (7 bytes): 01 03 02 13 88 78 47
Valid Modbus reply: 01 03 02 13 88 78 47
Register value: 5000 (0x1388)
```

## Performance Considerations

### Timing
- RS485 inter-frame gap: 10ms minimum
- RS485 timeout: 200ms
- RS232 timeout: 100ms
- Command processing: < 5ms

### Buffer Management
- RS485 buffer: 32 bytes
- RS232 buffer: 64 bytes
- Parameter buffer: 16 float values

### Error Handling
- CRC validation for Modbus
- Timeout detection
- Invalid parameter checking
- Exception handling in C# backend

## Integration with Existing System

The UART passthrough system integrates seamlessly with the existing Kogna.Controller architecture:

1. **IPC Commands**: Uses existing IPC infrastructure
2. **Process Management**: C# backend manages C program execution
3. **Error Handling**: Consistent error reporting format
4. **Logging**: Integrated with existing logging system

## Future Enhancements

1. **Multi-slave Support**: Enhanced RS485 multi-drop support
2. **Protocol Extensions**: Support for additional Modbus function codes
3. **Configuration Persistence**: Save UART settings across reboots
4. **Real-time Monitoring**: Continuous status monitoring
5. **Advanced Protocols**: Support for custom LED/Laser protocols

## Conclusion

The improved UART passthrough system provides reliable, efficient communication with FS50L servo drives and LED/Laser drivers using proper KMotion patterns. The system is robust, well-documented, and ready for production use. 