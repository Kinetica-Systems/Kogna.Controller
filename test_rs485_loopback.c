/*
 * RS485 Loopback Test
 * Simple test to verify RS485 hardware is working
 */

#include "KMotionDef.h"

#define RS485_BAUD 38400

main()
{
    unsigned char test_data[] = {0x01, 0x02, 0x03, 0x04, 0x05};
    unsigned char rx_data[32];
    int i, rxlen;
    double t0;
    
    printf("RS485 Loopback Test\n");
    printf("==================\n");
    
    /* Configure RS485 */
    printf("Configuring RS485 at %d baud...\n", RS485_BAUD);
    EnableRS422Cmds(RS485_BAUD);
    DoRS422Cmds = FALSE;
    RS422_SetBaudRate(RS485_BAUD, 8, FALSE, FALSE, TRUE);
    
    /* Flush buffer */
    printf("Flushing RX buffer...\n");
    while (pRS422RecIn != pRS422RecOut) {
        RS422_GetChar();
    }
    
    /* Send test data */
    printf("Sending test data: ");
    for (i = 0; i < 5; i++) {
        RS422_PutChar(test_data[i]);
        printf("%02X ", test_data[i]);
    }
    printf("\n");
    
    /* Wait a bit */
    Delay_sec(0.01);
    
    /* Read response */
    printf("Reading response...\n");
    rxlen = 0;
    t0 = Time_sec();
    
    while ((Time_sec() - t0) < 0.1 && rxlen < 32) {
        if (pRS422RecIn != pRS422RecOut) {
            rx_data[rxlen] = RS422_GetChar();
            rxlen++;
        } else {
            WaitNextTimeSlice();
        }
    }
    
    printf("Received %d bytes: ", rxlen);
    for (i = 0; i < rxlen; i++) {
        printf("%02X ", rx_data[i]);
    }
    printf("\n");
    
    if (rxlen > 0) {
        printf("Hardware is responding (may be echo)\n");
    } else {
        printf("No response from hardware\n");
    }
    
    printf("Test completed\n");
} 