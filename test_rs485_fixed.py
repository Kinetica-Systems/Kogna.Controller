#!/usr/bin/env python3
"""
Test RS485 communication with fixed timeout handling
Tests the updated C program with proper error handling
"""

import socket
import json
import time
import sys

def test_rs485_fixed():
    """Test RS485 communication with fixed timeout handling"""
    
    print("🔧 Test RS485 Communication (Fixed Version)")
    print("=" * 45)
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(15)  # Increased timeout for testing
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
        
        # Test 1: Basic RS485 read with timeout protection
        print("\n🔍 Test 1: RS485 Read with Timeout Protection")
        
        # Set up RS485 read command (slave=1, register=0x3002, value=0 for read)
        cmd = {
            "Command": "rs485",
            "Args": ["1", "0x3002", "0"]  # Slave 1, Bus Voltage register, read
        }
        
        message = json.dumps(cmd) + '\n'
        print(f"📤 Sending: {cmd}")
        
        sock.send(message.encode('utf-8'))
        
        # Wait for response with timeout
        try:
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            # Parse response
            try:
                result = json.loads(response)
                if "Result" in result:
                    value = result["Result"]
                    print(f"✅ RS485 Read Result: {value}")
                    if value > 0:
                        print(f"   Bus Voltage: {value} (0x{value:04X})")
                    elif value == 0:
                        print("   ⚠️  No response or timeout")
                    elif value == -1:
                        print("   ❌ Function timeout error")
                else:
                    print(f"   ⚠️  Unexpected response format: {result}")
            except json.JSONDecodeError:
                print(f"   ⚠️  Non-JSON response: {response}")
                
        except socket.timeout:
            print("   ❌ Timeout waiting for response")
        
        # Test 2: RS485 write command
        print("\n🔍 Test 2: RS485 Write Command")
        
        cmd = {
            "Command": "rs485", 
            "Args": ["1", "0x3001", "100"]  # Slave 1, some register, write value 100
        }
        
        message = json.dumps(cmd) + '\n'
        print(f"📤 Sending: {cmd}")
        
        sock.send(message.encode('utf-8'))
        
        try:
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            try:
                result = json.loads(response)
                if "Result" in result:
                    value = result["Result"]
                    print(f"✅ RS485 Write Result: {value}")
                    if value == 1:
                        print("   ✅ Write command acknowledged")
                    elif value == 0:
                        print("   ⚠️  Write command failed or no response")
                    elif value == -1:
                        print("   ❌ Function timeout error")
                else:
                    print(f"   ⚠️  Unexpected response format: {result}")
            except json.JSONDecodeError:
                print(f"   ⚠️  Non-JSON response: {response}")
                
        except socket.timeout:
            print("   ❌ Timeout waiting for response")
        
        # Test 3: Invalid slave address (should use default)
        print("\n🔍 Test 3: Invalid Slave Address (should use default)")
        
        cmd = {
            "Command": "rs485",
            "Args": ["0", "0x3002", "0"]  # Invalid slave 0, should default to 1
        }
        
        message = json.dumps(cmd) + '\n'
        print(f"📤 Sending: {cmd}")
        
        sock.send(message.encode('utf-8'))
        
        try:
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            try:
                result = json.loads(response)
                if "Result" in result:
                    value = result["Result"]
                    print(f"✅ Invalid Slave Result: {value}")
                    if value > 0:
                        print("   ✅ Used default slave address successfully")
                    else:
                        print("   ⚠️  Default slave address failed")
                else:
                    print(f"   ⚠️  Unexpected response format: {result}")
            except json.JSONDecodeError:
                print(f"   ⚠️  Non-JSON response: {response}")
                
        except socket.timeout:
            print("   ❌ Timeout waiting for response")
        
        sock.close()
        print("\n✅ All tests completed")
        
    except ConnectionRefusedError:
        print("❌ Could not connect to IPC server")
        print("   Make sure the Kogna.Controller app is running and connected")
    except Exception as e:
        print(f"❌ Error: {e}")

if __name__ == "__main__":
    test_rs485_fixed() 