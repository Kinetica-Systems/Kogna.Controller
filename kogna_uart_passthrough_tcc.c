/*
 * Kogna UART Passthrough Program - TCC Compatible Version
 * 
 * This program handles UART communication for FS50L servo drives (RS485) 
 * and LED/Laser drivers (RS232) using proper KMotion patterns.
 * 
 * Compiled with TCC (Tiny C Compiler) for Kogna controller
 */

#include "KMotionDef.h"

/* Configuration constants */
#define RS485_BAUD     38400      /* RS-485 baud rate for FS50L drives */
#define RS232_BAUD     115200     /* RS-232 baud rate for LED/Laser drivers */
#define RS485_TIMEOUT  200        /* ms timeout for RS-485 response */
#define RS232_TIMEOUT  100        /* ms timeout for RS-232 response */
#define DEFAULT_SLAVE  1          /* default Modbus slave ID */

/* Global variables for parameter passing */
float f_command, f_port, f_slave, f_register, f_value, f_length;
int command, port, slave, register_addr, value, data_length;

/* Function prototypes */
unsigned short ModbusCRC16(const unsigned char* buf, int len);
void send_rs485_modbus(int slave_id, int register_addr, int value, int is_write);
void send_rs232_command(const unsigned char* data, int length);
void configure_uart(int port_type, int baud_rate);

/* Modbus RTU CRC16 calculation */
unsigned short ModbusCRC16(const unsigned char* buf, int len)
{
    unsigned short crc = 0xFFFF;
    int i, j;
    for (i = 0; i < len; i++) {
        crc = crc ^ buf[i];
        for (j = 0; j < 8; j++) {
            if (crc & 1)
                crc = (crc >> 1) ^ 0xA001;
            else
                crc = crc >> 1;
        }
    }
    return crc;
}

/* Configure UART settings */
void configure_uart(int port_type, int baud_rate)
{
    if (port_type == 1) { /* RS485 */
        printf("Configuring RS485: %d baud\n", baud_rate);
        EnableRS422Cmds(baud_rate);
        DoRS422Cmds = FALSE;
        RS422_SetBaudRate(baud_rate, 8, FALSE, FALSE, TRUE); /* TRUE = RS485 mode */
    } else if (port_type == 2) { /* RS232 */
        printf("Configuring RS232: %d baud\n", baud_rate);
        EnableRS422Cmds(baud_rate);
        DoRS422Cmds = FALSE;
        RS422_SetBaudRate(baud_rate, 8, FALSE, FALSE, FALSE); /* FALSE = RS232 mode */
    }
}

/* Send Modbus RTU command over RS485 */
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
    int flush_count = 0;
    int max_flush = 1000; /* Prevent infinite flush loop */
    
    printf("RS485 Modbus: Slave=%d, Reg=0x%04X, Val=%d, Write=%d\n", 
           slave_id, register_addr, value, is_write);

    /* Null pointer check for RS422 pointers */
    if (pRS422RecIn == NULL || pRS422RecOut == NULL) {
        printf("ERROR: RS422 pointers are NULL!\n");
        persist.UserData[10] = -2;
        return;
    }

    /* Inter-frame gap (required by Modbus RTU spec) */
    Delay_sec(0.01);

    /* Build Modbus RTU frame dynamically based on parameters */
    function_code = is_write ? 0x06 : 0x03; /* Write single register : Read holding registers */
    
    tx[0] = slave_id;
    tx[1] = function_code;
    tx[2] = (register_addr >> 8) & 0xFF;  /* Register high byte */
    tx[3] = register_addr & 0xFF;          /* Register low byte */
    
    if (is_write) {
        tx[4] = (value >> 8) & 0xFF;       /* Value high byte */
        tx[5] = value & 0xFF;              /* Value low byte */
    } else {
        tx[4] = 0x00;                      /* Quantity high byte */
        tx[5] = 0x01;                      /* Quantity low byte (read 1 register) */
    }

    /* Calculate and append CRC */
    crc = ModbusCRC16(tx, 6);
    tx[6] = crc & 0xFF;
    tx[7] = (crc >> 8) & 0xFF;

    /* Flush RX buffer with timeout protection */
    printf("Before RX buffer flush: pRS422RecIn=%p, pRS422RecOut=%p\n", pRS422RecIn, pRS422RecOut);
    flush_count = 0;
    while (pRS422RecIn != pRS422RecOut && flush_count < max_flush) {
        RS422_GetChar();
        flush_count++;
        WaitNextTimeSlice(); /* Allow other tasks to run */
    }
    printf("After RX buffer flush, flushed %d bytes\n", flush_count);
    if (flush_count >= max_flush) {
        printf("Warning: RX flush timeout, continuing anyway\n");
    }

    /* Send request */
    printf("Before sending request\n");
    printf("TX: ");
    for (i = 0; i < 8; i++) {
        RS422_PutChar(tx[i]);
        printf("%02X ", tx[i]);
    }
    printf("\nAfter sending request\n");

    /* Wait for reply with shorter timeout */
    Delay_sec(0.02); /* Reduced from 0.05 to 0.02 */

    /* Read reply with better timeout handling */
    printf("Before reading reply\n");
    rxlen = 0;
    t0 = Time_sec();
    int timeout_count = 0;
    int max_timeout_checks = 50; /* Prevent infinite loop */
    
    while ((Time_sec() - t0) < (RS485_TIMEOUT / 1000.0) && rxlen < 32 && timeout_count < max_timeout_checks) {
        if (pRS422RecIn != pRS422RecOut) {
            rx[rxlen] = RS422_GetChar();
            rxlen = rxlen + 1;
            timeout_count = 0; /* Reset timeout counter when we get data */
        } else {
            WaitNextTimeSlice();
            timeout_count++;
        }
    }
    printf("After reading reply, rxlen=%d, timeout_count=%d\n", rxlen, timeout_count);
    if (timeout_count >= max_timeout_checks) {
        printf("Warning: RX timeout protection triggered\n");
    }

    printf("RX (%d bytes): ", rxlen);
    for (i = 0; i < rxlen; i++) {
        printf("%02X ", rx[i]);
    }
    printf("\n");

    /* Validate and decode Modbus reply */
    for (i = 0; i < rxlen - 4; i++) {
        if (rx[i] == slave_id) {
            /* Check for normal response (0x03, 0x06) or exception response (0x83, 0x86) */
            if (rx[i+1] == 0x03 || rx[i+1] == 0x06) {
                bytes = rx[i+2];
                if (i + 3 + bytes + 2 <= rxlen) {
                    frame = &rx[i];
                    framelen = 3 + bytes + 2;
                    crc = ModbusCRC16(frame, framelen - 2);
                    crc_reply = frame[framelen-2] | (frame[framelen-1] << 8);
                    
                    if (crc == crc_reply) {
                        printf("Valid Modbus reply at RX[%d]: ", i);
                        for (j = 0; j < framelen; j++) {
                            printf("%02X ", frame[j]);
                        }
                        printf("\n");
                        
                        /* For single register read */
                        if (rx[i+1] == 0x03 && bytes == 2) {
                            reg_value = (frame[3] << 8) | frame[4];
                            printf("Register value: %d (0x%04X)\n", reg_value, reg_value);
                            
                            /* Write result to persist data */
                            persist.UserData[10] = reg_value;
                            printf("RESULT: %d\n", reg_value);
                            return; /* Success - exit function */
                        }
                        /* For write confirmation */
                        else if (rx[i+1] == 0x06) {
                            printf("Write command acknowledged\n");
                            persist.UserData[10] = 1; /* Indicate success */
                            printf("RESULT: 1\n");
                            return; /* Success - exit function */
                        }
                    }
                }
            }
            /* Check for exception response (function code + 0x80) */
            else if (rx[i+1] == 0x83 || rx[i+1] == 0x86) {
                if (i + 5 <= rxlen) {
                    frame = &rx[i];
                    framelen = 5; /* Exception response is always 5 bytes */
                    crc = ModbusCRC16(frame, framelen - 2);
                    crc_reply = frame[framelen-2] | (frame[framelen-1] << 8);
                    
                    if (crc == crc_reply) {
                        printf("Valid Modbus exception response at RX[%d]: ", i);
                        for (j = 0; j < framelen; j++) {
                            printf("%02X ", frame[j]);
                        }
                        printf("\n");
                        printf("Exception code: 0x%02X\n", frame[2]);
                        
                        /* Store negative exception code to indicate error */
                        persist.UserData[10] = -frame[2];
                        printf("RESULT: %d (exception)\n", -frame[2]);
                        return; /* Exception handled - exit function */
                    }
                }
            }
        }
    }
    
    /* If we get here, no valid response was received */
    printf("No valid Modbus response received\n");
    persist.UserData[10] = 0; /* Indicate failure */
    printf("RESULT: 0\n");
}

/* Send RS232 command */
void send_rs232_command(const unsigned char* data, int length)
{
    unsigned char rx[64];
    int rxlen;
    double t0;
    int i;
    static int rs232_configured = 0;
    
    printf("RS232: Sending %d bytes\n", length);

    /* Configure RS232 if not already done */
    if (rs232_configured == 0) {
        EnableRS422Cmds(RS232_BAUD);
        DoRS422Cmds = FALSE;
        RS422_SetBaudRate(RS232_BAUD, 8, FALSE, FALSE, FALSE); /* FALSE = RS232 mode */
        rs232_configured = 1;
    }

    /* Flush RX buffer */
    while (pRS422RecIn != pRS422RecOut) {
        RS422_GetChar();
    }

    /* Send data */
    printf("TX: ");
    for (i = 0; i < length; i++) {
        RS422_PutChar(data[i]);
        printf("%02X ", data[i]);
    }
    printf("\n");

    /* Wait for reply */
    Delay_sec(0.02);

    /* Read reply */
    rxlen = 0;
    t0 = Time_sec();
    
    while ((Time_sec() - t0) < (RS232_TIMEOUT / 1000.0) && rxlen < 64) {
        if (pRS422RecIn != pRS422RecOut) {
            rx[rxlen] = RS422_GetChar();
            rxlen = rxlen + 1;
        } else {
            WaitNextTimeSlice();
        }
    }

    if (rxlen > 0) {
        printf("RX (%d bytes): ", rxlen);
        for (i = 0; i < rxlen; i++) {
            printf("%02X ", rx[i]);
        }
        printf("\n");
    } else {
        printf("No reply received\n");
    }
}

/* Main function - M-code handler */
main()
{
    unsigned char rs232_data[64];
    int data_len;
    int i;
    double start_time;
    
    /* Add safety timeout for entire function */
    start_time = Time_sec();
    
    /* Initialize RS485 port */
    EnableRS422Cmds(RS485_BAUD);
    DoRS422Cmds = FALSE;
    RS422_SetBaudRate(RS485_BAUD, 8, FALSE, FALSE, TRUE); /* TRUE = RS485 mode */
    
    /* Fetch parameters from persist mailbox - read as integers directly */
    /* This matches the working RS485_Push_test.c approach */
    
    /* Debug: Print raw persist data */
    printf("Raw persist data: UserData[0]=%d, UserData[1]=%d, UserData[2]=%d\n", 
           persist.UserData[0], persist.UserData[1], persist.UserData[2]);

    /* Read persist data as integers directly (no float conversion) */
    slave = persist.UserData[0];         /* Slave ID */
    register_addr = persist.UserData[1]; /* Register address */
    value = persist.UserData[2];         /* Value to write */

    printf("Converted values: Slave: %d, Reg: %d, Val: %d\n", slave, register_addr, value);

    /* Initialize result to -999 to track if it gets set */
    persist.UserData[10] = -999;
    printf("DEBUG: Initialized persist.UserData[10] = %d\n", persist.UserData[10]);

    /* Apply defaults and validation like working version */
    if (slave <1|| slave > 247) {
        slave = DEFAULT_SLAVE;
        printf("Invalid slave address, using default: %d\n", slave);
    }
    
    /* Check timeout before calling RS485function */
    if ((Time_sec() - start_time) > 1.0) {
        printf("ERROR: Function timeout before RS485 call\n");
        persist.UserData[10] = -1; /* Error code */
        return;
    }
    
    send_rs485_modbus(slave, register_addr, value, (value != 0));

    printf("Function completed in %.3f seconds\n", Time_sec() - start_time);
    printf("DEBUG: Final persist.UserData[10] = %d\n", persist.UserData[10]);
    WaitNextTimeSlice();
} 