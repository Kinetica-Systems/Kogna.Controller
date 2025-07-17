# Kogna UART Passthrough C Program

This directory contains the C program that needs to be compiled and flashed to the Kogna controller to handle M100-M102 UART passthrough commands.

## Files

- `kogna_uart_passthrough.c` - Main C program
- `uart_config.h` - Configuration header file
- `Makefile` - Build configuration
- `README_C_PROGRAM.md` - This file

## Prerequisites

### On Development Machine
- GCC compiler
- Make utility
- SSH access to Kogna controller

### On Kogna Controller
- Linux-based system
- UART/Serial ports available
- USB-to-Serial adapters (if needed)

## Hardware Setup

### RS485 Setup (FS50L Servo Drives)
1. Connect USB-to-RS485 adapter to Kogna
2. Connect RS485 adapter to FS50L servo drives
3. Set appropriate baudrate (typically 115200)
4. Configure slave addresses on servo drives (1-247)

### RS232 Setup (LED/Laser Drivers)
1. Connect USB-to-RS232 adapter to Kogna
2. Connect RS232 adapter to LED/Laser drivers
3. Set appropriate baudrate (typically 9600)
4. Configure device addresses

## Compilation

### Local Compilation
```bash
# Compile the program
make all

# Test compilation
make test

# Clean build artifacts
make clean
```

### Cross-Compilation (if needed)
```bash
# Set cross-compiler
export CC=arm-linux-gnueabihf-gcc

# Compile
make all
```

## Deployment

### Method 1: Direct Copy
```bash
# Copy to Kogna
scp kogna_uart_passthrough kogna@192.168.0.50:/usr/local/bin/

# Set permissions
ssh kogna@192.168.0.50 "chmod +x /usr/local/bin/kogna_uart_passthrough"
```

### Method 2: Using Makefile
```bash
# Edit Makefile to add your flashing command
# Then run:
make flash
```

### Method 3: Package Installation
```bash
# Create a simple package
tar -czf kogna-uart-passthrough.tar.gz kogna_uart_passthrough

# Copy to Kogna
scp kogna-uart-passthrough.tar.gz kogna@192.168.0.50:/tmp/

# Install on Kogna
ssh kogna@192.168.0.50 "cd /tmp && tar -xzf kogna-uart-passthrough.tar.gz && sudo cp kogna_uart_passthrough /usr/local/bin/ && sudo chmod +x /usr/local/bin/kogna_uart_passthrough"
```

## Configuration

### Device Paths
Edit `uart_config.h` to match your hardware setup:

```c
// Adjust these paths based on your USB-to-Serial adapters
#define RS485_DEVICE "/dev/ttyUSB0"  // RS485 device
#define RS232_DEVICE "/dev/ttyUSB1"  // RS232 device
```

### Baudrates
```c
// Adjust baudrates based on your devices
#define RS485_DEFAULT_BAUDRATE B115200  // FS50L default
#define RS232_DEFAULT_BAUDRATE B9600    // LED/Laser default
```

## Testing

### Test on Kogna
```bash
# Test RS485 read
./kogna_uart_passthrough "M100 1 3001"

# Test RS485 write
./kogna_uart_passthrough "M100 1 1000 1"

# Test RS232
./kogna_uart_passthrough "M101 1 1000 255"

# Test configuration
./kogna_uart_passthrough "M102 rs485 115200 1"
```

### Integration Testing
1. Start the Kogna.Controller application
2. Connect to hardware
3. Test via terminal:
   ```bash
   rs485 1 3001
   servostatus 1 frequency
   servocontrol 1 forward
   ```

## Troubleshooting

### Common Issues

#### 1. Permission Denied
```bash
# Fix device permissions
sudo chmod 666 /dev/ttyUSB*
sudo usermod -a -G dialout kogna
```

#### 2. Device Not Found
```bash
# Check available devices
ls -la /dev/ttyUSB*
ls -la /dev/ttyACM*

# Check USB devices
lsusb
```

#### 3. Communication Timeout
- Check baudrate settings
- Verify cable connections
- Check device addresses
- Verify Modbus protocol settings

#### 4. CRC Errors
- Check wiring for RS485
- Verify termination resistors
- Check for electrical noise

### Debug Mode
Enable debug output by setting in `uart_config.h`:
```c
#define DEBUG_ENABLED 1
```

## Integration with Kogna

### M Command Mapping
The Kogna controller should be configured to execute this program when M100-M102 commands are received:

- **M100**: Execute `kogna_uart_passthrough "M100 <params>"`
- **M101**: Execute `kogna_uart_passthrough "M101 <params>"`
- **M102**: Execute `kogna_uart_passthrough "M102 <params>"`

### Kogna Configuration
You may need to configure the Kogna to:
1. Recognize M100-M102 commands
2. Execute the C program with parameters
3. Capture and return program output

## Security Considerations

1. **File Permissions**: Ensure the program has appropriate permissions
2. **User Access**: Run as appropriate user (not root if possible)
3. **Input Validation**: The program validates all inputs
4. **Error Handling**: Comprehensive error handling included

## Performance

- **Response Time**: < 100ms for typical operations
- **Memory Usage**: < 1MB
- **CPU Usage**: Minimal during idle, brief spikes during communication

## Maintenance

### Updates
1. Modify source code as needed
2. Recompile: `make clean && make all`
3. Deploy: `make flash` or copy manually
4. Test: Run test commands

### Logs
The program outputs to stdout/stderr. Capture logs if needed:
```bash
./kogna_uart_passthrough "M100 1 3001" 2>&1 | tee uart.log
```

## Support

For issues with the C program:
1. Check hardware connections
2. Verify device paths in `uart_config.h`
3. Test with known good devices
4. Enable debug mode for detailed output
5. Check Kogna system logs

## License

This program is part of the Kogna.Controller project and follows the same license terms. 