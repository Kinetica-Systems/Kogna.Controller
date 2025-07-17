#!/usr/bin/env python3
"""
Quick FS50L Test Script
Simple test for FS50L servo drive on address 1
"""

import socket
import time

def test_fs50l():
    # Connect to Kogna
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    
    try:
        print("Connecting to Kogna...")
        sock.connect(('localhost', 5000))
        print("Connected!")
        
        def send_cmd(cmd):
            print(f"\nSending: {cmd}")
            sock.send((cmd + "\n").encode())
            response = sock.recv(1024).decode().strip()
            print(f"Response: {response}")
            return response
        
        # Test 1: Configure RS485
        print("\n=== Test 1: Configure RS485 ===")
        send_cmd("uartconfig rs485 38400")
        time.sleep(1)
        
        # Test 2: Read running frequency
        print("\n=== Test 2: Read Running Frequency ===")
        send_cmd("rs485 1 3001")
        time.sleep(0.5)
        
        # Test 3: Read bus voltage
        print("\n=== Test 3: Read Bus Voltage ===")
        send_cmd("rs485 1 3002")
        time.sleep(0.5)
        
        # Test 4: Read output current
        print("\n=== Test 4: Read Output Current ===")
        send_cmd("rs485 1 3004")
        time.sleep(0.5)
        
        # Test 5: Read fault status
        print("\n=== Test 5: Read Fault Status ===")
        send_cmd("rs485 1 8000")
        time.sleep(0.5)
        
        print("\n=== Basic Tests Complete ===")
        
        # Optional: Test control commands
        print("\n" + "="*50)
        print("WARNING: Control tests will attempt to control the servo!")
        print("Make sure the system is safe!")
        print("="*50)
        
        proceed = input("\nTest control commands? (y/N): ").strip().lower()
        if proceed == 'y':
            print("\n=== Control Tests ===")
            
            # Free stop
            print("\n--- Free Stop ---")
            send_cmd("rs485 1 1000 5")
            time.sleep(1)
            
            # Fault reset
            print("\n--- Fault Reset ---")
            send_cmd("rs485 1 1000 7")
            time.sleep(1)
            
            # Set frequency to 10Hz (1000 in register)
            print("\n--- Set Frequency to 10Hz ---")
            send_cmd("rs485 1 3000 1000")
            time.sleep(1)
            
            # Read back frequency
            print("\n--- Read Frequency Setting ---")
            send_cmd("rs485 1 3000")
            time.sleep(0.5)
            
            # Forward run
            print("\n--- Forward Run ---")
            send_cmd("rs485 1 1000 1")
            time.sleep(2)
            
            # Free stop
            print("\n--- Free Stop ---")
            send_cmd("rs485 1 1000 5")
            time.sleep(1)
            
            print("\n=== Control Tests Complete ===")
        else:
            print("Skipping control tests.")
        
    except Exception as e:
        print(f"Error: {e}")
    finally:
        sock.close()
        print("Connection closed")

if __name__ == "__main__":
    test_fs50l() 