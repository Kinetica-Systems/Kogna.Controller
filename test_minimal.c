/*
 * Minimal Test Program
 * Tests basic C program execution without RS485
 */

#include "KMotionDef.h"

main()
{
    int i;
    
    printf("Minimal test program started\n");
    
    /* Just read persist data and print it */
    printf("Persist data check:\n");
    for (i = 0; i < 5; i++) {
        printf("UserData[%d] = %d\n", i, persist.UserData[i]);
    }
    
    /* Set a simple result */
    persist.UserData[10] = 12345;
    printf("Set UserData[10] = 12345\n");
    
    printf("Minimal test completed successfully\n");
} 