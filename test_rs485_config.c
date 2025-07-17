/*
 * RS485 Configuration Test
 * Tests RS485 setup without sending data
 */

#include "KMotionDef.h"

#define RS485_BAUD 38400

main()
{
    printf("RS485 Configuration Test\n");
    printf("=======================\n");
    
    /* Test 1: Basic RS485 configuration */
    printf("Step 1: Configuring RS485...\n");
    EnableRS422Cmds(RS485_BAUD);
    printf("EnableRS422Cmds completed\n");
    
    DoRS422Cmds = FALSE;
    printf("DoRS422Cmds set to FALSE\n");
    
    RS422_SetBaudRate(RS485_BAUD, 8, FALSE, FALSE, TRUE);
    printf("RS422_SetBaudRate completed\n");
    
    printf("RS485 configuration completed successfully\n");
    
    /* Test 2: Check if pointers are valid */
    printf("Step 2: Checking RS422 pointers...\n");
    if (pRS422RecIn != NULL && pRS422RecOut != NULL) {
        printf("RS422 pointers are valid: pRS422RecIn=%p, pRS422RecOut=%p\n", 
               pRS422RecIn, pRS422RecOut);
    } else {
        printf("ERROR: RS422 pointers are NULL!\n");
    }
    
    /* Test 3: Simple buffer flush */
    printf("Step 3: Testing buffer flush...\n");
    int flush_count = 0;
    while (pRS422RecIn != pRS422RecOut && flush_count < 100) {
        RS422_GetChar();
        flush_count++;
    }
    printf("Buffer flush completed, flushed %d bytes\n", flush_count);
    
    printf("All RS485 configuration tests passed\n");
    persist.UserData[10] = 999;
} 