#!/usr/bin/env python3
"""
Test script to verify C program works standalone on Kogna
This script helps debug the UART passthrough system
"""

import socket
import json
import time

def test_c_program_standalone():
    """Test if the C program can be executed and returns results"""
    
    print("🔧 Testing C Program Standalone Execution")
    print("=" * 50)
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
    except Exception as e:
        print(f"❌ Failed to connect: {e}")
        return
    
    # Test 1: Set persist data manually and execute thread
    print("\n🔍 Test 1: Manual persist data setup")
    
    # Set up persist data for RS485 read command
    commands = [
        {"Command": "SetPersist UserData[0] 1", "Args": []},  # Command type: RS485
        {"Command": "SetPersist UserData[2] 1", "Args": []},  # Slave address: 1
        {"Command": "SetPersist UserData[3] 12290", "Args": []},  # Register: 0x3002 (bus voltage)
        {"Command": "SetPersist UserData[4] 0", "Args": []},  # Value: 0 (read)
        {"Command": "ExecThread 3", "Args": []},  # Execute C program
    ]
    
    for i, cmd in enumerate(commands):
        try:
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: {cmd['Command']}")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            # Wait a bit between commands
            time.sleep(0.1)
            
        except Exception as e:
            print(f"❌ Command failed: {e}")
    
    # Test 2: Read the result
    print("\n🔍 Test 2: Read result from persist data")
    
    try:
        cmd = {"Command": "GetPersist UserData[10]", "Args": []}
        message = json.dumps(cmd) + '\n'
        sock.send(message.encode('utf-8'))
        print(f"📤 Sent: {cmd['Command']}")
        
        response = sock.recv(1024).decode('utf-8').strip()
        print(f"📥 Received: {response}")
        
        # Parse response
        try:
            response_data = json.loads(response)
            if response_data.get('Status') == 'OK':
                result = response_data.get('Result', 'No result')
                print(f"✅ Result: {result}")
                
                # Try to parse as integer
                try:
                    result_int = int(result)
                    if result_int > 0:
                        print(f"✅ Valid result: {result_int}")
                    else:
                        print(f"⚠️ Result is zero - device may not be responding")
                except ValueError:
                    print(f"⚠️ Result is not a number: {result}")
            else:
                print(f"❌ Error: {response_data.get('Error', 'Unknown error')}")
        except json.JSONDecodeError:
            print(f"⚠️ Raw response: {response}")
            
    except Exception as e:
        print(f"❌ Test failed: {e}")
    
    # Test 3: Check console output
    print("\n🔍 Test 3: Check console output")
    
    try:
        cmd = {"Command": "ServiceConsole", "Args": []}
        message = json.dumps(cmd) + '\n'
        sock.send(message.encode('utf-8'))
        print(f"📤 Sent: {cmd['Command']}")
        
        response = sock.recv(1024).decode('utf-8').strip()
        print(f"📥 Received: {response}")
        
        # Parse response
        try:
            response_data = json.loads(response)
            if response_data.get('Status') == 'OK':
                console_output = response_data.get('Result', 'No output')
                print(f"📋 Console output: {console_output}")
            else:
                print(f"❌ Error: {response_data.get('Error', 'Unknown error')}")
        except json.JSONDecodeError:
            print(f"⚠️ Raw response: {response}")
            
    except Exception as e:
        print(f"❌ Test failed: {e}")
    
    sock.close()
    print("\n🏁 Test completed")

if __name__ == "__main__":
    test_c_program_standalone() 