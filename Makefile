# Makefile for Kogna UART Passthrough Program
# Compile and flash to Kogna controller

CC = gcc
CFLAGS = -Wall -Wextra -std=c99 -O2
TARGET = kogna_uart_passthrough
SOURCE = kogna_uart_passthrough.c

# Default target
all: $(TARGET)

# Compile the program
$(TARGET): $(SOURCE)
	$(CC) $(CFLAGS) -o $(TARGET) $(SOURCE)

# Clean build artifacts
clean:
	rm -f $(TARGET)

# Install to Kogna (adjust path as needed)
install: $(TARGET)
	cp $(TARGET) /usr/local/bin/
	chmod +x /usr/local/bin/$(TARGET)

# Flash to Kogna (adjust path as needed)
flash: $(TARGET)
	# Add your specific flashing command here
	# Example: scp $(TARGET) kogna@192.168.0.50:/usr/local/bin/
	echo "Please implement your specific flashing command"

# Test compilation
test: $(TARGET)
	./$(TARGET) "M100 1 3001"
	./$(TARGET) "M101 1 1000 255"
	./$(TARGET) "M102 rs485 115200 1"

# Help
help:
	@echo "Available targets:"
	@echo "  all     - Build the program"
	@echo "  clean   - Remove build artifacts"
	@echo "  install - Install to system"
	@echo "  flash   - Flash to Kogna (implement your command)"
	@echo "  test    - Run test commands"
	@echo "  help    - Show this help"

.PHONY: all clean install flash test help 