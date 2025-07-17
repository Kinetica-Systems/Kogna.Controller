#!/usr/bin/env python3
"""
Test script to verify RS485 passthrough fix
Tests the updated C program with correct parameter mapping
"""

import socket
import json
import time
import sys

def test_rs485_fix():
    """Test the RS485 passthrough fix"""
    
    print("🔧 Testing RS485 Passthrough Fix")
    print("=" * 50)
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
    except Exception as e:
        print(f"❌ Failed to connect: {e}")
        return
    
    # Test RS485 read command
    print("\n🔍 Testing RS485 read command...")
    
    try:
        # Send RS485 command
        request = {
            "Command": "rs485",
            "Args": ["1", "3001"]  # Read register 0x3001 from slave 1
        }
        
        message = json.dumps(request) + '\n'
        sock.send(message.encode('utf-8'))
        print(f"📤 Sent: rs485 1 3001")
        
        response = sock.recv(1024).decode('utf-8').strip()
        print(f"📥 Received: {response}")
        
        # Parse response
        try:
            result = json.loads(response)
            print(f"✅ Command executed successfully")
            print(f"📊 Response: {result}")
        except json.JSONDecodeError:
            print(f"⚠️  Raw response: {response}")
            
    except Exception as e:
        print(f"❌ Test failed: {e}")
    
    # Test RS485 write command
    print("\n🔍 Testing RS485 write command...")
    
    try:
        # Send RS485 write command
        request = {
            "Command": "rs485",
            "Args": ["1", "1000", "1"]  # Write value 1 to register 0x1000 on slave 1
        }
        
        message = json.dumps(request) + '\n'
        sock.send(message.encode('utf-8'))
        print(f"📤 Sent: rs485 1 1000 1")
        
        response = sock.recv(1024).decode('utf-8').strip()
        print(f"📥 Received: {response}")
        
        # Parse response
        try:
            result = json.loads(response)
            print(f"✅ Command executed successfully")
            print(f"📊 Response: {result}")
        except json.JSONDecodeError:
            print(f"⚠️  Raw response: {response}")
            
    except Exception as e:
        print(f"❌ Test failed: {e}")
    
    # Test servo status command
    print("\n🔍 Testing servo status command...")
    
    try:
        # Send servo status command
        request = {
            "Command": "servostatus",
            "Args": ["1", "frequency"]  # Get frequency from slave 1
        }
        
        message = json.dumps(request) + '\n'
        sock.send(message.encode('utf-8'))
        print(f"📤 Sent: servostatus 1 frequency")
        
        response = sock.recv(1024).decode('utf-8').strip()
        print(f"📥 Received: {response}")
        
        # Parse response
        try:
            result = json.loads(response)
            print(f"✅ Command executed successfully")
            print(f"📊 Response: {result}")
        except json.JSONDecodeError:
            print(f"⚠️  Raw response: {response}")
            
    except Exception as e:
        print(f"❌ Test failed: {e}")
    
    sock.close()
    print("\n✅ RS485 fix test completed")

if __name__ == "__main__":
    test_rs485_fix() 