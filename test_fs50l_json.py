#!/usr/bin/env python3
"""
FS50L Test Script using JSON format for IpcServer
"""

import socket
import json
import time

class KognaJSONTester:
    def __init__(self, host='localhost', port=5000):
        self.host = host
        self.port = port
        self.socket = None
        
    def connect(self):
        """Connect to Kogna controller"""
        try:
            self.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.socket.connect((self.host, self.port))
            print(f"Connected to Kogna at {self.host}:{self.port}")
            return True
        except Exception as e:
            print(f"Failed to connect: {e}")
            return False
    
    def send_json_command(self, command, args=None):
        """Send JSON command to Kogna and get response"""
        if not self.socket:
            print("Not connected")
            return None
            
        try:
            # Create JSON request
            request = {
                "Command": command,
                "Args": args or []
            }
            
            # Send JSON request
            json_request = json.dumps(request) + "\n"
            print(f"Sending: {json_request.strip()}")
            self.socket.send(json_request.encode('utf-8'))
            
            # Get response
            response = self.socket.recv(1024).decode('utf-8').strip()
            print(f"Response: {response}")
            
            # Parse JSON response
            try:
                json_response = json.loads(response)
                return json_response
            except:
                return {"Status": "ERROR", "Result": response}
                
        except Exception as e:
            print(f"Command failed: {e}")
            return None
    
    def test_rs485_read(self, slave_addr, register):
        """Test RS485 read operation"""
        print(f"\n--- Testing RS485 Read: Slave {slave_addr}, Register 0x{register:04X} ---")
        
        response = self.send_json_command("rs485", [str(slave_addr), f"{register:04X}"])
        return response
    
    def test_rs485_write(self, slave_addr, register, value):
        """Test RS485 write operation"""
        print(f"\n--- Testing RS485 Write: Slave {slave_addr}, Register 0x{register:04X}, Value {value} ---")
        
        response = self.send_json_command("rs485", [str(slave_addr), f"{register:04X}", str(value)])
        return response
    
    def test_uart_config(self, uart_type, baudrate):
        """Test UART configuration"""
        print(f"\n--- Testing UART Config: {uart_type} at {baudrate} baud ---")
        
        response = self.send_json_command("uartconfig", [uart_type, str(baudrate)])
        return response
    
    def test_fs50l_basic(self):
        """Test basic FS50L communication"""
        print("\n" + "="*60)
        print("FS50L BASIC COMMUNICATION TEST")
        print("="*60)
        
        # Test 1: Configure RS485
        print("\n1. Configuring RS485...")
        self.test_uart_config("rs485", 38400)
        time.sleep(1)
        
        # Test 2: Read running frequency (register 0x3001)
        print("\n2. Reading running frequency...")
        self.test_rs485_read(1, 0x3001)
        time.sleep(0.5)
        
        # Test 3: Read bus voltage (register 0x3002)
        print("\n3. Reading bus voltage...")
        self.test_rs485_read(1, 0x3002)
        time.sleep(0.5)
        
        # Test 4: Read output current (register 0x3004)
        print("\n4. Reading output current...")
        self.test_rs485_read(1, 0x3004)
        time.sleep(0.5)
        
        # Test 5: Read fault status (register 0x8000)
        print("\n5. Reading fault status...")
        self.test_rs485_read(1, 0x8000)
        time.sleep(0.5)
        
        print("\n=== Basic Tests Complete ===")
    
    def test_fs50l_control(self):
        """Test FS50L control commands"""
        print("\n" + "="*60)
        print("FS50L CONTROL COMMANDS TEST")
        print("="*60)
        
        # Test 1: Free stop command
        print("\n1. Sending free stop command...")
        self.test_rs485_write(1, 0x1000, 0x0005)
        time.sleep(1)
        
        # Test 2: Fault reset command
        print("\n2. Sending fault reset command...")
        self.test_rs485_write(1, 0x1000, 0x0007)
        time.sleep(1)
        
        # Test 3: Set frequency to 10Hz (1000 in register)
        print("\n3. Setting frequency to 10Hz...")
        self.test_rs485_write(1, 0x3000, 1000)
        time.sleep(1)
        
        # Test 4: Read back frequency setting
        print("\n4. Reading back frequency setting...")
        self.test_rs485_read(1, 0x3000)
        time.sleep(0.5)
        
        # Test 5: Forward run command
        print("\n5. Sending forward run command...")
        self.test_rs485_write(1, 0x1000, 0x0001)
        time.sleep(2)
        
        # Test 6: Free stop again
        print("\n6. Sending free stop command...")
        self.test_rs485_write(1, 0x1000, 0x0005)
        time.sleep(1)
        
        print("\n=== Control Tests Complete ===")
    
    def close(self):
        """Close connection"""
        if self.socket:
            self.socket.close()
            print("Connection closed")

def main():
    print("FS50L Servo Drive JSON Communication Test")
    print("="*50)
    
    # Create tester
    tester = KognaJSONTester('localhost', 5000)
    
    # Connect
    if not tester.connect():
        print("Failed to connect. Exiting.")
        return
    
    try:
        # Run basic tests
        print("\nStarting FS50L basic communication tests...")
        tester.test_fs50l_basic()
        
        # Control tests (be careful with these!)
        print("\n" + "="*60)
        print("WARNING: Control tests will attempt to control the servo drive!")
        print("Make sure the system is safe and the servo is not under load.")
        print("="*60)
        
        proceed = input("\nProceed with control tests? (y/N): ").strip().lower()
        if proceed == 'y':
            tester.test_fs50l_control()
        else:
            print("Skipping control tests.")
        
        print("\n" + "="*60)
        print("TEST COMPLETE")
        print("="*60)
        
    except KeyboardInterrupt:
        print("\nTest interrupted by user")
    except Exception as e:
        print(f"\nTest failed with error: {e}")
    finally:
        tester.close()

if __name__ == "__main__":
    main() 