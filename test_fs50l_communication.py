#!/usr/bin/env python3
"""
FS50L Servo Drive Communication Test
Tests the UART passthrough system with an FS50L servo drive on address 1
"""

import socket
import time
import struct
import sys

class KognaUARTTester:
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
    
    def send_command(self, command):
        """Send command to Kogna and get response"""
        if not self.socket:
            print("Not connected")
            return None
            
        try:
            # Add newline to command
            full_command = command + "\n"
            self.socket.send(full_command.encode('utf-8'))
            
            # Get response
            response = self.socket.recv(1024).decode('utf-8').strip()
            return response
        except Exception as e:
            print(f"Command failed: {e}")
            return None
    
    def test_rs485_read(self, slave_addr, register):
        """Test RS485 read operation"""
        print(f"\n--- Testing RS485 Read: Slave {slave_addr}, Register 0x{register:04X} ---")
        
        command = f"rs485 {slave_addr} {register:04X}"
        print(f"Sending: {command}")
        
        response = self.send_command(command)
        if response:
            print(f"Response: {response}")
            return response
        else:
            print("No response received")
            return None
    
    def test_rs485_write(self, slave_addr, register, value):
        """Test RS485 write operation"""
        print(f"\n--- Testing RS485 Write: Slave {slave_addr}, Register 0x{register:04X}, Value {value} ---")
        
        command = f"rs485 {slave_addr} {register:04X} {value}"
        print(f"Sending: {command}")
        
        response = self.send_command(command)
        if response:
            print(f"Response: {response}")
            return response
        else:
            print("No response received")
            return None
    
    def test_uart_config(self, uart_type, baudrate):
        """Test UART configuration"""
        print(f"\n--- Testing UART Config: {uart_type} at {baudrate} baud ---")
        
        command = f"uartconfig {uart_type} {baudrate}"
        print(f"Sending: {command}")
        
        response = self.send_command(command)
        if response:
            print(f"Response: {response}")
            return response
        else:
            print("No response received")
            return None
    
    def test_fs50l_registers(self):
        """Test common FS50L registers"""
        print("\n" + "="*60)
        print("FS50L SERVO DRIVE COMMUNICATION TEST")
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
        
        # Test 5: Read output power (register 0x3005)
        print("\n5. Reading output power...")
        self.test_rs485_read(1, 0x3005)
        time.sleep(0.5)
        
        # Test 6: Read running speed (register 0x3007)
        print("\n6. Reading running speed...")
        self.test_rs485_read(1, 0x3007)
        time.sleep(0.5)
        
        # Test 7: Read fault information (register 0x8000)
        print("\n7. Reading fault information...")
        self.test_rs485_read(1, 0x8000)
        time.sleep(0.5)
        
        # Test 8: Read communication fault (register 0x8001)
        print("\n8. Reading communication fault...")
        self.test_rs485_read(1, 0x8001)
        time.sleep(0.5)
        
        # Test 9: Test control commands (read current control state)
        print("\n9. Reading current control state...")
        self.test_rs485_read(1, 0x1000)
        time.sleep(0.5)
        
        # Test 10: Test frequency setting (read current frequency setting)
        print("\n10. Reading current frequency setting...")
        self.test_rs485_read(1, 0x3000)
        time.sleep(0.5)
    
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
        
        # Test 3: Forward run command
        print("\n3. Sending forward run command...")
        self.test_rs485_write(1, 0x1000, 0x0001)
        time.sleep(2)
        
        # Test 4: Set frequency to 25Hz (2500 in register)
        print("\n4. Setting frequency to 25Hz...")
        self.test_rs485_write(1, 0x3000, 2500)
        time.sleep(1)
        
        # Test 5: Read back the frequency setting
        print("\n5. Reading back frequency setting...")
        self.test_rs485_read(1, 0x3000)
        time.sleep(0.5)
        
        # Test 6: Free stop again
        print("\n6. Sending free stop command...")
        self.test_rs485_write(1, 0x1000, 0x0005)
        time.sleep(1)
    
    def test_error_conditions(self):
        """Test error conditions and edge cases"""
        print("\n" + "="*60)
        print("ERROR CONDITIONS TEST")
        print("="*60)
        
        # Test 1: Invalid slave address
        print("\n1. Testing invalid slave address (0)...")
        self.test_rs485_read(0, 0x3001)
        time.sleep(0.5)
        
        # Test 2: Invalid slave address (248)
        print("\n2. Testing invalid slave address (248)...")
        self.test_rs485_read(248, 0x3001)
        time.sleep(0.5)
        
        # Test 3: Non-existent register
        print("\n3. Testing non-existent register (0xFFFF)...")
        self.test_rs485_read(1, 0xFFFF)
        time.sleep(0.5)
        
        # Test 4: Invalid UART config
        print("\n4. Testing invalid UART config...")
        self.test_uart_config("invalid", 9600)
        time.sleep(0.5)
    
    def close(self):
        """Close connection"""
        if self.socket:
            self.socket.close()
            print("Connection closed")

def main():
    print("FS50L Servo Drive UART Passthrough Test")
    print("="*50)
    
    # Get connection details
    host = input("Enter Kogna IP address (default: localhost): ").strip()
    if not host:
        host = 'localhost'
    
    port_str = input("Enter Kogna port (default: 5000): ").strip()
    if not port_str:
        port = 5000
    else:
        try:
            port = int(port_str)
        except ValueError:
            print("Invalid port number, using 5000")
            port = 5000
    
    # Create tester
    tester = KognaUARTTester(host, port)
    
    # Connect
    if not tester.connect():
        print("Failed to connect. Exiting.")
        return
    
    try:
        # Run tests
        print("\nStarting FS50L communication tests...")
        
        # Basic register tests
        tester.test_fs50l_registers()
        
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
        
        # Error condition tests
        tester.test_error_conditions()
        
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