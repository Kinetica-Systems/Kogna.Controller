#!/usr/bin/env python3
"""
Test script to verify RS485 command with terminal output capture
"""

import socket
import json
import time

def test_rs485_with_terminal_output():
    """Test the RS485 command with terminal output capture"""
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
    except Exception as e:
        print(f"❌ Failed to connect: {e}")
        return
    
    # Test 1: RS485 read command
    print("\n🔍 Test 1: RS485 read command")
    request = {
        "Command": "rs485",
        "Args": ["1", "3001"]
    }
    
    try:
        # Send the request
        message = json.dumps(request) + '\n'
        sock.send(message.encode('utf-8'))
        print(f"📤 Sent: {request['Command']} {' '.join(request['Args'])}")
        
        # Get response
        response = sock.recv(1024).decode('utf-8').strip()
        print(f"📥 Received: {response}")
        
        # Parse the response
        try:
            response_data = json.loads(response)
            if response_data.get('Status') == 'OK':
                print(f"✅ Success: {response_data.get('Result', 'No result')}")
            else:
                print(f"❌ Error: {response_data.get('Error', 'Unknown error')}")
        except json.JSONDecodeError:
            print(f"⚠️ Raw response: {response}")
            
    except Exception as e:
        print(f"❌ Test failed: {e}")
    
    # Test 2: RS485 write command
    print("\n🔍 Test 2: RS485 write command")
    request = {
        "Command": "rs485",
        "Args": ["1", "3000", "1000"]  # Write value 1000 to register 3000
    }
    
    try:
        # Send the request
        message = json.dumps(request) + '\n'
        sock.send(message.encode('utf-8'))
        print(f"📤 Sent: {request['Command']} {' '.join(request['Args'])}")
        
        # Get response
        response = sock.recv(1024).decode('utf-8').strip()
        print(f"📥 Received: {response}")
        
        # Parse the response
        try:
            response_data = json.loads(response)
            if response_data.get('Status') == 'OK':
                print(f"✅ Success: {response_data.get('Result', 'No result')}")
            else:
                print(f"❌ Error: {response_data.get('Error', 'Unknown error')}")
        except json.JSONDecodeError:
            print(f"⚠️ Raw response: {response}")
            
    except Exception as e:
        print(f"❌ Test failed: {e}")
    
    sock.close()
    print("\n🏁 Test completed")

if __name__ == "__main__":
    test_rs485_with_terminal_output() 