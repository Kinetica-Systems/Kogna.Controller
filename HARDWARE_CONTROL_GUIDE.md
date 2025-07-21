# Hardware Control Guide for Kogna.Controller

## Overview

This guide explains how to control 2x lasers using PWM and a wire feeder using step/dir signals with the Kogna and Kanalog boards.

## Hardware Architecture

### Pin Assignment Strategy

**Primary 8 Channels (0-7) - Real-time Motion Control:**
- Channel 0: X-axis analog output (±10V)
- Channel 1: Y-axis analog output (±10V)  
- Channel 2: Z-axis analog output (±10V)
- Channel 3: A-axis analog output (±10V)
- Channel 4: B-axis analog output (±10V)
- Channel 5: C-axis analog output (±10V)
- Channel 6: Reserved for future axis
- Channel 7: Reserved for future axis

**Secondary 8 Channels (8-15) - PWM and Hardware Control:**
- Channel 8:  Laser 1 PWM output (0-10V)
- Channel 9:  Laser 2 PWM output (0-10V)
- Channel 10: Wire feeder step signal (digital)
- Channel 11: Wire feeder direction signal (digital)
- Channel 12: Spare PWM output
- Channel 13: Spare PWM output
- Channel 14: Spare PWM output
- Channel 15: Spare PWM output

## Control Methods

### 1. G-code Commands

#### M42 - Set Pin State (PWM Control)
```gcode
M42 P8 S128    ; Set Laser 1 to 50% power (channel 8)
M42 P9 S255    ; Set Laser 2 to 100% power (channel 9)
M42 P10 S1     ; Set wire feeder step high
M42 P11 S0     ; Set wire feeder direction low
```

#### Standard Laser Commands
```gcode
M3              ; Turn on spindle/laser (standard)
M5              ; Turn off spindle/laser (standard)
```

### 2. IPC Commands (via TCP/IP)

#### Laser Control
```bash
laser 1 on      ; Turn on laser 1 (full power)
laser 2 off     ; Turn off laser 2
laser 1 128     ; Set laser 1 to 50% power (0-255)
laser 2 255     ; Set laser 2 to 100% power
```

#### Wire Feeder Control
```bash
wirefeeder step high    ; Set wire feeder step high
wirefeeder step low     ; Set wire feeder step low
wirefeeder dir high     ; Set wire feeder direction high
wirefeeder dir low      ; Set wire feeder direction low
```

#### Direct PWM Control
```bash
pwm 8 128       ; Set channel 8 to 50% PWM (0-255)
pwm 9 255       ; Set channel 9 to 100% PWM
pwm 12 64       ; Set channel 12 to 25% PWM
```

## Usage Examples

### Laser Cutting Program
```gcode
G21             ; Set units to mm
G90             ; Absolute positioning
G0 X0 Y0        ; Move to start position
M42 P8 S255     ; Turn on laser 1 at full power
G1 X100 Y0 F100 ; Cut line at 100mm/min
M42 P8 S0       ; Turn off laser 1
G0 X0 Y0        ; Return to start
```

### Wire Feeder Control
```gcode
G21             ; Set units to mm
G90             ; Absolute positioning
M42 P11 S0      ; Set direction low
M42 P10 S1      ; Step high
M42 P10 S0      ; Step low (one step)
M42 P10 S1      ; Step high
M42 P10 S0      ; Step low (another step)
```

### Combined Operation
```gcode
G21             ; Set units to mm
G90             ; Absolute positioning
M42 P8 S128     ; Set laser 1 to 50% power
M42 P9 S64      ; Set laser 2 to 25% power
G1 X100 Y100 F200 ; Move with both lasers on
M42 P8 S0       ; Turn off laser 1
M42 P9 S0       ; Turn off laser 2
```

## Safety Features

### Emergency Stop
- All PWM outputs are automatically set to 0 on emergency stop
- Motion is immediately halted
- Hardware outputs are disabled

### Power Management
- PWM outputs are limited to 0-255 range
- Automatic shutdown on fault conditions
- Soft limits prevent over-current conditions

## Troubleshooting

### Common Issues

1. **Laser not responding:**
   - Check channel assignment (8 for laser 1, 9 for laser 2)
   - Verify PWM value is 0-255
   - Test with `pwm 8 128` command

2. **Wire feeder not moving:**
   - Check step/dir connections (channels 10-11)
   - Verify step signal timing
   - Test with `wirefeeder step high` command

3. **PWM output issues:**
   - Verify channel is not used by motion control
   - Check voltage range (0-10V)
   - Test with direct M42 commands

### Testing Commands

```bash
# Test laser 1
laser 1 on
laser 1 128
laser 1 off

# Test laser 2  
laser 2 on
laser 2 64
laser 2 off

# Test wire feeder
wirefeeder step high
wirefeeder step low
wirefeeder dir high
wirefeeder dir low

# Test direct PWM
pwm 8 255
pwm 8 0
pwm 9 128
pwm 9 0
```

## Hardware Setup

### Laser Connections
- Laser 1: Connect to channel 8 PWM output
- Laser 2: Connect to channel 9 PWM output
- Power supply: 0-10V analog signal
- Ground: Common ground with Kogna system

### Wire Feeder Connections
- Step signal: Connect to channel 10 digital output
- Direction signal: Connect to channel 11 digital output
- Enable signal: Use channel 12 if needed
- Power: External power supply for stepper motor

### Safety Considerations
- Always test at low power first
- Use appropriate safety equipment
- Monitor temperature and current
- Have emergency stop procedures ready

## Advanced Features

### Synchronized Operations
```gcode
G1 X100 Y100 F200 M42 P8 S128 M42 P9 S64 ; Move with both lasers
```

### Programmed Sequences
```gcode
; Laser cutting sequence
M42 P8 S255     ; Full power laser 1
G1 X50 Y0 F100  ; Cut first line
M42 P8 S128     ; Half power
G1 X50 Y50 F50  ; Cut second line
M42 P8 S0       ; Turn off
```

### Real-time Control
```bash
# Real-time laser power adjustment
pwm 8 255       ; Full power
pwm 8 128       ; Half power  
pwm 8 64        ; Quarter power
pwm 8 0         ; Off
```

## Integration with Existing System

The hardware control features integrate seamlessly with the existing 6-axis motion control system:

- **Motion Control**: Uses channels 0-7 for real-time positioning
- **Hardware Control**: Uses channels 8-15 for PWM and digital outputs
- **G-code Compatibility**: Standard M42 commands work with existing parsers
- **Safety Integration**: Emergency stops affect both motion and hardware outputs

This architecture provides clean separation between motion control and hardware control while maintaining full compatibility with existing G-code programs. 