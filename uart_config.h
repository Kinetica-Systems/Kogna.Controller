/*
 * UART Configuration Header
 * 
 * Configuration settings for RS485 and RS232 communication
 * Adjust these settings based on your hardware setup
 */

#ifndef UART_CONFIG_H
#define UART_CONFIG_H

// Device paths - adjust based on your setup
#define RS485_DEVICE "/dev/ttyUSB0"  // RS485 device for FS50L servo drives
#define RS232_DEVICE "/dev/ttyUSB1"  // RS232 device for LED/Laser drivers

// Default baudrates
#define RS485_DEFAULT_BAUDRATE B115200  // FS50L typically uses 115200
#define RS232_DEFAULT_BAUDRATE B9600    // LED/Laser drivers often use 9600

// Timeout settings
#define UART_READ_TIMEOUT_MS 1000       // 1 second timeout for reads
#define UART_WRITE_TIMEOUT_MS 500       // 500ms timeout for writes

// Buffer sizes
#define MAX_UART_BUFFER_SIZE 256
#define MAX_MODBUS_FRAME_SIZE 256

// Modbus settings
#define MODBUS_MAX_SLAVE_ADDR 247
#define MODBUS_MIN_SLAVE_ADDR 1

// Function codes
#define MODBUS_READ_HOLDING_REGISTERS 0x03
#define MODBUS_WRITE_SINGLE_REGISTER 0x06
#define MODBUS_WRITE_MULTIPLE_REGISTERS 0x10

// FS50L Register addresses (from the manual)
#define FS50L_RUNNING_FREQUENCY 0x3001
#define FS50L_BUS_VOLTAGE 0x3002
#define FS50L_OUTPUT_CURRENT 0x3004
#define FS50L_OUTPUT_POWER 0x3005
#define FS50L_OUTPUT_TORQUE 0x3006
#define FS50L_RUNNING_SPEED 0x3007
#define FS50L_DRIVE_FAULT 0x8000
#define FS50L_COMM_FAULT 0x8001

// FS50L Control registers
#define FS50L_CONTROL_COMMAND 0x1000
#define FS50L_FREQUENCY_SETTING 0x3000

// Control command values
#define FS50L_CMD_FORWARD_RUN 0x0001
#define FS50L_CMD_REVERSE_RUN 0x0002
#define FS50L_CMD_FORWARD_JOG 0x0003
#define FS50L_CMD_REVERSE_JOG 0x0004
#define FS50L_CMD_FREE_STOP 0x0005
#define FS50L_CMD_DECEL_STOP 0x0006
#define FS50L_CMD_FAULT_RESET 0x0007

// LED/Laser driver settings
#define LED_MAX_POWER 255
#define LED_MIN_POWER 0

// Error codes
#define UART_SUCCESS 0
#define UART_ERROR_DEVICE -1
#define UART_ERROR_CONFIG -2
#define UART_ERROR_TIMEOUT -3
#define UART_ERROR_CRC -4
#define UART_ERROR_PARAM -5

// Debug settings
#define DEBUG_ENABLED 1
#define DEBUG_PRINT(fmt, ...) if(DEBUG_ENABLED) printf("[DEBUG] " fmt "\n", ##__VA_ARGS__)

#endif // UART_CONFIG_H 