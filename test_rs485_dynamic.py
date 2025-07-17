#!/usr/bin/env python3
"""
Test script to verify RS485 passthrough with dynamic register addressing
Tests different register addresses to ensure the C program builds frames correctly
"""

import socket
import json
import time
import sys

def test_rs485_dynamic():
    """Test RS485 passthrough with dynamic register addressing"""
    
    print("🔧 Testing RS485 Dynamic Register Addressing")
    print("=" * 60)
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
    except Exception as e:
        print(f"❌ Failed to connect: {e}")
        return
    
    # Test different register addresses
    test_cases = [
        ("Read frequency (0x3001)", "1", "3001", None),
        ("Read voltage (0x3002)", "1", "3002", None),
        ("Read current (0x3004)", "1", "3004", None),
        ("Read power (0x3005)", "1", "3005", None),
        ("Write control (0x1000)", "1", "1000", "1"),
        ("Write frequency (0x3000)", "1", "3000", "5000"),
    ]
    
    for test_name, slave, register, value in test_cases:
        print(f"\n🔍 Testing: {test_name}")
        print(f"   Slave: {slave}, Register: 0x{int(register):04X}, Value: {value}")
        
        try:
            # Build command
            if value is None:
                # Read command
                request = {
                    "Command": "rs485",
                    "Args": [slave, register]
                }
                cmd_str = f"rs485 {slave} {register}"
            else:
                # Write command
                request = {
                    "Command": "rs485", 
                    "Args": [slave, register, value]
                }
                cmd_str = f"rs485 {slave} {register} {value}"
            
            # Send command
            message = json.dumps(request) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: {cmd_str}")
            
            # Receive response
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            # Parse response
            try:
                result = json.loads(response)
                if isinstance(result, dict) and 'Status' in result:
                    if result['Status'] == 'OK':
                        print(f"✅ Success: {result.get('Result', 'No result')}")
                    else:
                        print(f"❌ Error: {result.get('Error', 'Unknown error')}")
                else:
                    print(f"📊 Response: {result}")
            except json.JSONDecodeError:
                print(f"⚠️  Raw response: {response}")
                
        except Exception as e:
            print(f"❌ Test failed: {e}")
        
        # Wait between tests
        time.sleep(1)
    
    # Test servo status commands
    print(f"\n🔍 Testing servo status commands...")
    
    status_tests = [
        ("frequency", "3001"),
        ("voltage", "3002"), 
        ("current", "3004"),
        ("power", "3005"),
        ("torque", "3006"),
        ("speed", "3007"),
    ]
    
    for status_name, register in status_tests:
        print(f"\n🔍 Testing servostatus: {status_name}")
        
        try:
            request = {
                "Command": "servostatus",
                "Args": ["1", status_name]
            }
            
            message = json.dumps(request) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: servostatus 1 {status_name}")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            try:
                result = json.loads(response)
                print(f"📊 Response: {result}")
            except json.JSONDecodeError:
                print(f"⚠️  Raw response: {response}")
                
        except Exception as e:
            print(f"❌ Test failed: {e}")
        
        time.sleep(0.5)
    
    sock.close()
    print("\n✅ Dynamic register addressing test completed")

if __name__ == "__main__":
    test_rs485_dynamic() 