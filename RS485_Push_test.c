	#include "KMotionDef.h"
/*

		printf("P = %f Q = %f R = %f\n",
			*(float *)&persist.UserData[0],
			*(float *)&persist.UserData[1],
			*(float *)&persist.UserData[2]);
	
*/

float f_reg, f_val, f_slave, f_isWrite;
int reg, val, slave, isWrite;



#define RS485_BAUD     38400      // RS-485 baud rate
#define RS485_TIMEOUT  200        // ms timeout for RS-485 response
#define CONSOLE_BAUD   115200    // USB-CDC console baud rate
#define DEFAULT_SLAVE  1         // default Modbus slave ID




//── Thread3(): M-code handler at 1 kHz ───────────────────────────────────────
main()
{
    
    // --- Initialize RS485 port for 9600 baud, 8N1, no parity ---
    EnableRS422Cmds(38400);
    DoRS422Cmds = FALSE;
    RS422_SetBaudRate(38400, 8, FALSE, FALSE, TRUE); // last TRUE = RS485 mode

    // --- Inter-frame gap: wait at least 10ms of silence before frame (required by Modbus RTU spec) ---
    Delay_sec(0.01);
            
    // Fetch parameters from persist mailbox
    f_reg      = *(float *)&persist.UserData[1];        // register address
    f_val      = *(float *)&persist.UserData[2];        // value to write
    f_slave    = *(float *)&persist.UserData[0];        // slave ID

    printf("%f %f %f \n", f_slave, f_reg, f_val ); //debug track
            
    //convert float to int
    reg =   (int)f_reg;
    val =   (int)f_val;
    slave = (int)f_slave;

    //check if theres a value to write to the unit, otherwise 0 equals a read request

    if (f_val == 0)
        isWrite = 0;

    if (f_val != 0)
        isWrite = 1;
    

    printf("%d %d %d %d\n", slave, reg, val, isWrite);     //debug track integers
    printf("%x %x %x %d\n", slave, reg, val, isWrite);     //debug track the integers displayed as a hex


    // Apply defaults and clamp
    if (slave < 0 || slave > 247)
        slave = DEFAULT_SLAVE;
    int RWbyte = (isWrite ? 0x06 : 0x03);

    // --- Build Modbus RTU request for: Read Holding Register 0x3001 (Quantity: 1) ---
    unsigned char tx[8] = { 0x01, 0x03, 0xFD, 0x00, 0x00, 0x01, 0x00, 0x00 };
    unsigned short crc = ModbusCRC16(tx, 6);
    tx[6] = crc & 0xFF;
    tx[7] = (crc >> 8) & 0xFF;

    // --- Flush RX buffer ---
	while (pRS422RecIn != pRS422RecOut) RS422_GetChar();

    // --- Send request ---
    int i;
    printf("Raw TX: ");
    
	for (i = 0; i < 8; ++i) 
	{
		RS422_PutChar(tx[i]);
		printf("%02X ", tx[i]);
	}
	
	printf("\n");

    // --- Wait for reply ---
    Delay_sec(0.05); // Wait 50ms for drive to reply

    // --- Read reply ---
    unsigned char rx[32];
    int rxlen = 0;
    double t0 = Time_sec();
    while ((Time_sec() - t0) < 0.2 && rxlen < sizeof(rx)) {
        if (pRS422RecIn != pRS422RecOut)
            rx[rxlen++] = RS422_GetChar();
        else
            WaitNextTimeSlice();
    }

    printf("Raw RX (%d bytes): ", rxlen);
    for (i = 0; i < rxlen; ++i) printf("%02X ", rx[i]);
    printf("\n");

    // --- (Optional: decode a valid Modbus reply) ---
    for (i = 0; i < rxlen - 4; ++i) {
        if (rx[i] == 0x01 && rx[i+1] == 0x03) {
            int bytes = rx[i+2];
            if (i + 3 + bytes + 2 <= rxlen) {
                unsigned char* frame = &rx[i];
                int framelen = 3 + bytes + 2;
                unsigned short crc = ModbusCRC16(frame, framelen - 2);
                unsigned short crc_reply = frame[framelen-2] | (frame[framelen-1] << 8);
                if (crc == crc_reply) {
                    printf("Valid Modbus reply at RX[%d]: ", i);
                    int j;
                    for (j = 0; j < framelen; ++j)
                        printf("%02X ", frame[j]);
                    printf("\n");
                    // For single register:
                    if (bytes == 2)
                        printf("Register value: %u (0x%04X)\n", (frame[3]<<8) | frame[4], (frame[3]<<8) | frame[4]);
                }
            }
        }
    }



        WaitNextTimeSlice();

        persist.UserData[0] = 0; //clear registers for next frame
        persist.UserData[1] = 0;
        persist.UserData[2] = 0;
        persist.UserData[3] = 0;
    }




// --- Modbus RTU CRC16 (standard) ---
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
