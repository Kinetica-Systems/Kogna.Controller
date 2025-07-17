/*
 * Kogna UART Passthrough Program
 * 
 * This program handles M100-M102 commands for UART communication:
 * M100 - RS485 Read/Write (FS50L servo drives)
 * M101 - RS232 Read/Write (LED/Laser drivers)  
 * M102 - UART Configuration
 * 
 * Compile and flash to Kogna controller
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <unistd.h>
#include <fcntl.h>
#include <termios.h>
#include <sys/ioctl.h>
#include <errno.h>
#include "KMotionDef.h"

// Configuration constants
#define RS485_BAUD     38400      // RS-485 baud rate for FS50L drives
#define RS232_BAUD     115200     // RS-232 baud rate for LED/Laser drivers
#define RS485_TIMEOUT  200        // ms timeout for RS-485 response
#define RS232_TIMEOUT  100        // ms timeout for RS-232 response
#define DEFAULT_SLAVE  1          // default Modbus slave ID

// Global variables for parameter passing
float f_command, f_port, f_slave, f_register, f_value, f_length;
int command, port, slave, register_addr, value, data_length;

// Function prototypes
unsigned short ModbusCRC16(const unsigned char* buf, int len);
void send_rs485_modbus(int slave_id, int register_addr, int value, int is_write);
void send_rs232_command(const unsigned char* data, int length);
void configure_uart(int port_type, int baud_rate);

// Main function - M-code handler
main()
{
    // Fetch parameters from persist mailbox
    f_command = *(float *)&persist.UserData[0];    // Command type (1=RS485, 2=RS232, 3=config)
    f_port = *(float *)&persist.UserData[1];       // Port configuration
    f_slave = *(float *)&persist.UserData[2];      // Slave ID (for RS485)
    f_register = *(float *)&persist.UserData[3];   // Register address (for RS485)
    f_value = *(float *)&persist.UserData[4];      // Value to write (for RS485)
    f_length = *(float *)&persist.UserData[5];     // Data length (for RS232)

    // Convert float parameters to integers
    command = (int)f_command;
    port = (int)f_port;
    slave = (int)f_slave;
    register_addr = (int)f_register;
    value = (int)f_value;
    data_length = (int)f_length;

    printf("Command: %d, Port: %d, Slave: %d, Reg: %d, Val: %d, Len: %d\n", 
           command, port, slave, register_addr, value, data_length);

    switch (command) {
        case 1: // RS485 Modbus communication
            if (slave < 1 || slave > 247) slave = DEFAULT_SLAVE;
            send_rs485_modbus(slave, register_addr, value, (value != 0));
            break;
            
        case 2: // RS232 communication
            {
                // Extract RS232 data from persist buffer
                unsigned char rs232_data[64];
                int data_len = 0;
                for (int i = 0; i < data_length && i < 64; i++) {
                    rs232_data[i] = (unsigned char)(*(float *)&persist.UserData[6 + i]);
                    data_len++;
                }
                send_rs232_command(rs232_data, data_len);
            }
            break;
            
        case 3: // UART configuration
            configure_uart(port, value);
            break;
            
        default:
            printf("Unknown command: %d\n", command);
            break;
    }

    // Clear persist buffer for next command
    for (int i = 0; i < 16; i++) {
        persist.UserData[i] = 0;
    }

    WaitNextTimeSlice();
}

// Configure UART settings
void configure_uart(int port_type, int baud_rate)
{
    if (port_type == 1) { // RS485
        printf("Configuring RS485: %d baud\n", baud_rate);
        EnableRS422Cmds(baud_rate);
        DoRS422Cmds = FALSE;
        RS422_SetBaudRate(baud_rate, 8, FALSE, FALSE, TRUE); // TRUE = RS485 mode
    } else if (port_type == 2) { // RS232
        printf("Configuring RS232: %d baud\n", baud_rate);
        EnableRS422Cmds(baud_rate);
        DoRS422Cmds = FALSE;
        RS422_SetBaudRate(baud_rate, 8, FALSE, FALSE, FALSE); // FALSE = RS232 mode
    }
}

// Send Modbus RTU command over RS485
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
    int function_code = is_write ? 0x06 : 0x03; // Write single register : Read holding registers
    
    tx[0] = slave_id;
    tx[1] = function_code;
    tx[2] = (register_addr >> 8) & 0xFF;
    tx[3] = register_addr & 0xFF;
    
    if (is_write) {
        tx[4] = (value >> 8) & 0xFF;
        tx[5] = value & 0xFF;
    } else {
        tx[4] = 0x00; // Quantity high byte
        tx[5] = 0x01; // Quantity low byte (read 1 register)
    }

    // Calculate and append CRC
    unsigned short crc = ModbusCRC16(tx, 6);
    tx[6] = crc & 0xFF;
    tx[7] = (crc >> 8) & 0xFF;

    // Flush RX buffer
    while (pRS422RecIn != pRS422RecOut) RS422_GetChar();

    // Send request
    printf("TX: ");
    for (int i = 0; i < 8; i++) {
        RS422_PutChar(tx[i]);
        printf("%02X ", tx[i]);
    }
    printf("\n");

    // Wait for reply
    Delay_sec(0.05);

    // Read reply
    unsigned char rx[32];
    int rxlen = 0;
    double t0 = Time_sec();
    
    while ((Time_sec() - t0) < (RS485_TIMEOUT / 1000.0) && rxlen < sizeof(rx)) {
        if (pRS422RecIn != pRS422RecOut) {
            rx[rxlen++] = RS422_GetChar();
        } else {
            WaitNextTimeSlice();
        }
    }

    printf("RX (%d bytes): ", rxlen);
    for (int i = 0; i < rxlen; i++) {
        printf("%02X ", rx[i]);
    }
    printf("\n");

    // Validate and decode Modbus reply
    if (rxlen >= 5) {
        for (int i = 0; i < rxlen - 4; i++) {
            if (rx[i] == slave_id && (rx[i+1] == 0x03 || rx[i+1] == 0x06)) {
                int bytes = rx[i+2];
                if (i + 3 + bytes + 2 <= rxlen) {
                    unsigned char* frame = &rx[i];
                    int framelen = 3 + bytes + 2;
                    unsigned short crc = ModbusCRC16(frame, framelen - 2);
                    unsigned short crc_reply = frame[framelen-2] | (frame[framelen-1] << 8);
                    
                    if (crc == crc_reply) {
                        printf("Valid Modbus reply: ");
                        for (int j = 0; j < framelen; j++) {
                            printf("%02X ", frame[j]);
                        }
                        printf("\n");
                        
                        if (rx[i+1] == 0x03 && bytes == 2) {
                            int reg_value = (frame[3] << 8) | frame[4];
                            printf("Register value: %d (0x%04X)\n", reg_value, reg_value);
                        }
                    }
                }
            }
        }
    }
}

// Send RS232 command
void send_rs232_command(const unsigned char* data, int length)
{
    printf("RS232: Sending %d bytes\n", length);

    // Configure RS232 if not already done
    static int rs232_configured = 0;
    if (!rs232_configured) {
        EnableRS422Cmds(RS232_BAUD);
        DoRS422Cmds = FALSE;
        RS422_SetBaudRate(RS232_BAUD, 8, FALSE, FALSE, FALSE); // FALSE = RS232 mode
        rs232_configured = 1;
    }

    // Flush RX buffer
    while (pRS422RecIn != pRS422RecOut) RS422_GetChar();

    // Send data
    printf("TX: ");
    for (int i = 0; i < length; i++) {
        RS422_PutChar(data[i]);
        printf("%02X ", data[i]);
    }
    printf("\n");

    // Wait for reply
    Delay_sec(0.02);

    // Read reply
    unsigned char rx[64];
    int rxlen = 0;
    double t0 = Time_sec();
    
    while ((Time_sec() - t0) < (RS232_TIMEOUT / 1000.0) && rxlen < sizeof(rx)) {
        if (pRS422RecIn != pRS422RecOut) {
            rx[rxlen++] = RS422_GetChar();
        } else {
            WaitNextTimeSlice();
        }
    }

    if (rxlen > 0) {
        printf("RX (%d bytes): ", rxlen);
        for (int i = 0; i < rxlen; i++) {
            printf("%02X ", rx[i]);
        }
        printf("\n");
    } else {
        printf("No reply received\n");
    }
}

// Modbus RTU CRC16 calculation
unsigned short ModbusCRC16(const unsigned char* buf, int len)
{
    unsigned short crc = 0xFFFF;
    int i, j;
    for (i = 0; i < len; ++i) {
        crc ^= buf[i];
        for (j = 0; j < 8; ++j) {
            if (crc & 1)
                crc = (crc >> 1) ^ 0xA001;
            else
                crc = crc >> 1;
        }
    }
    return crc;
} 