#!/usr/bin/env python3
"""
Test script for Kogna.Controller UART passthrough features
Tests RS485 and RS232 communication for FS50L servo drives and LED/Laser drivers
"""

import socket
import json
import time
import sys

class KognaUartTester:
    def __init__(self, host='localhost', port=5000):
        self.host = host
        self.port = port
        self.socket = None
        
    def connect(self):
        """Connect to the Kogna controller"""
        try:
            self.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.socket.connect((self.host, self.port))
            print(f"Connected to {self.host}:{self.port}")
            return True
        except Exception as e:
            print(f"Connection failed: {e}")
            return False
    
    def send_command(self, command):
        """Send a command to the Kogna controller"""
        if not self.socket:
            print("Not connected")
            return None
            
        try:
            # Create IPC request
            request = {
                "Command": command,
                "Args": []
            }
            
            # Send request
            self.socket.send((json.dumps(request) + '\n').encode())
            
            # Receive response
            response = self.socket.recv(1024).decode().strip()
            return json.loads(response)
            
        except Exception as e:
            print(f"Command failed: {e}")
            return None
    
    def test_rs485_commands(self):
        """Test RS485 commands for FS50L servo drives"""
        print("\n=== Testing RS485 Commands (FS50L) ===")
        
        # Test reading servo status
        print("Testing servo status read...")
        response = self.send_command("rs485 1 3001")
        print(f"Read frequency: {response}")
        time.sleep(1)
        
        response = self.send_command("rs485 1 3002")
        print(f"Read voltage: {response}")
        time.sleep(1)
        
        response = self.send_command("rs485 1 3004")
        print(f"Read current: {response}")
        time.sleep(1)
        
        # Test writing servo commands
        print("Testing servo control write...")
        response = self.send_command("rs485 1 1000 0001")
        print(f"Forward run: {response}")
        time.sleep(1)
        
        response = self.send_command("rs485 1 1000 0005")
        print(f"Free stop: {response}")
        time.sleep(1)
    
    def test_rs232_commands(self):
        """Test RS232 commands for LED/Laser drivers"""
        print("\n=== Testing RS232 Commands (LED/Laser) ===")
        
        # Test reading LED status
        print("Testing LED status read...")
        response = self.send_command("rs232 1 1000")
        print(f"Read status: {response}")
        time.sleep(1)
        
        # Test writing LED commands
        print("Testing LED control write...")
        response = self.send_command("rs232 1 1000 255")
        print(f"Set power 255: {response}")
        time.sleep(1)
        
        response = self.send_command("rs232 1 1000 128")
        print(f"Set power 128: {response}")
        time.sleep(1)
    
    def test_convenience_commands(self):
        """Test convenience commands for FS50L"""
        print("\n=== Testing Convenience Commands ===")
        
        # Test servo status commands
        print("Testing servo status commands...")
        response = self.send_command("servostatus 1 frequency")
        print(f"Servo frequency: {response}")
        time.sleep(1)
        
        response = self.send_command("servostatus 1 voltage")
        print(f"Servo voltage: {response}")
        time.sleep(1)
        
        response = self.send_command("servostatus 1 current")
        print(f"Servo current: {response}")
        time.sleep(1)
        
        # Test servo control commands
        print("Testing servo control commands...")
        response = self.send_command("servocontrol 1 forward")
        print(f"Servo forward: {response}")
        time.sleep(1)
        
        response = self.send_command("servocontrol 1 free_stop")
        print(f"Servo stop: {response}")
        time.sleep(1)
        
        response = self.send_command("servocontrol 1 frequency 5000")
        print(f"Servo frequency 50%: {response}")
        time.sleep(1)
    
    def test_uart_config(self):
        """Test UART configuration commands"""
        print("\n=== Testing UART Configuration ===")
        
        # Test RS485 configuration
        print("Testing RS485 configuration...")
        response = self.send_command("uartconfig rs485 115200 1")
        print(f"RS485 config: {response}")
        time.sleep(1)
        
        # Test RS232 configuration
        print("Testing RS232 configuration...")
        response = self.send_command("uartconfig rs232 9600 2")
        print(f"RS232 config: {response}")
        time.sleep(1)
    
    def test_error_handling(self):
        """Test error handling for invalid commands"""
        print("\n=== Testing Error Handling ===")
        
        # Test invalid slave address
        print("Testing invalid slave address...")
        response = self.send_command("rs485 0 3001")
        print(f"Invalid address 0: {response}")
        time.sleep(1)
        
        response = self.send_command("rs485 248 3001")
        print(f"Invalid address 248: {response}")
        time.sleep(1)
        
        # Test missing parameters
        print("Testing missing parameters...")
        response = self.send_command("rs485 1")
        print(f"Missing register: {response}")
        time.sleep(1)
        
        response = self.send_command("servostatus 1")
        print(f"Missing status type: {response}")
        time.sleep(1)
    
    def run_all_tests(self):
        """Run all UART passthrough tests"""
        print("Starting Kogna UART Passthrough Tests")
        print("=" * 50)
        
        if not self.connect():
            print("Failed to connect. Make sure the Kogna controller is running.")
            return
        
        try:
            # Run all tests
            self.test_rs485_commands()
            self.test_rs232_commands()
            self.test_convenience_commands()
            self.test_uart_config()
            self.test_error_handling()
            
            print("\n" + "=" * 50)
            print("All tests completed!")
            
        except KeyboardInterrupt:
            print("\nTests interrupted by user")
        except Exception as e:
            print(f"Test error: {e}")
        finally:
            if self.socket:
                self.socket.close()
                print("Connection closed")

def main():
    """Main function"""
    if len(sys.argv) > 1:
        host = sys.argv[1]
    else:
        host = 'localhost'
    
    if len(sys.argv) > 2:
        port = int(sys.argv[2])
    else:
        port = 5000
    
    tester = KognaUartTester(host, port)
    tester.run_all_tests()

if __name__ == "__main__":
    main() 