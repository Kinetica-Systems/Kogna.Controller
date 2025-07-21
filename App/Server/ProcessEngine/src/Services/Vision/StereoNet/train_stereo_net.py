import os
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader
from torchvision import transforms
import numpy as np
from PIL import Image
import json
from datetime import datetime
import matplotlib.pyplot as plt
from typing import Tuple, List, Dict, Optional
import torch.nn.functional as F

class StereoBeadDataset(Dataset):
    """Dataset for loading stereo image pairs and corresponding bead geometry."""
    
    def __init__(self, root_dir: str, split: str = 'train', transform=None):
        """
        Args:
            root_dir: Directory with all the images and annotations
            split: One of 'train', 'val', or 'test'
            transform: Optional transform to be applied on a sample
        ""
        self.root_dir = os.path.join(root_dir, split)
        self.transform = transform or self._get_default_transform()
        self.samples = self._load_samples()
        
    def _load_samples(self) -> List[Dict]:
        samples = []
        left_dir = os.path.join(self.root_dir, 'left')
        right_dir = os.path.join(self.root_dir, 'right')
        
        for img_name in os.listdir(left_dir):
            if not img_name.lower().endswith(('.png', '.jpg', '.jpeg')):
                continue
                
            base_name = os.path.splitext(img_name)[0]
            right_img_path = os.path.join(right_dir, img_name)
            label_path = os.path.join(self.root_dir, 'labels', f"{base_name}.json")
            
            if os.path.exists(right_img_path) and os.path.exists(label_path):
                samples.append({
                    'left': os.path.join(left_dir, img_name),
                    'right': right_img_path,
                    'label': label_path
                })
                
        return samples
    
    def _get_default_transform(self):
        return transforms.Compose([
            transforms.Resize((256, 512)),  # Stereo pair as single image
            transforms.ToTensor(),
            transforms.Normalize(mean=[0.5, 0.5, 0.5], std=[0.5, 0.5, 0.5])
        ])
    
    def __len__(self):
        return len(self.samples)
    
    def __getitem__(self, idx: int) -> Tuple[torch.Tensor, Dict]:
        sample = self.samples[idx]
        
        # Load images
        left_img = Image.open(sample['left']).convert('RGB')
        right_img = Image.open(sample['right']).convert('RGB')
        
        # Stack images side by side (H, W, 6) -> (6, H, W) after transpose
        stereo_pair = torch.cat([
            self.transform(left_img),
            self.transform(right_img)
        ], dim=0)
        
        # Load labels
        with open(sample['label'], 'r') as f:
            labels = json.load(f)
        
        # Convert labels to tensors
        target = {
            'width': torch.tensor(labels.get('width', 0.0), dtype=torch.float32),
            'height': torch.tensor(labels.get('height', 0.0), dtype=torch.float32),
            'cross_section': torch.tensor(labels.get('cross_section', []), dtype=torch.float32),
            'defects': torch.tensor(labels.get('defects', []), dtype=torch.float32)
        }
        
        return stereo_pair, target

class StereoNet(nn.Module):
    """Neural network for 3D bead geometry analysis from stereo images."""
    
    def __init__(self, num_outputs: int = 4):
        super().__init__()
        
        # Feature extraction (shared weights for both images)
        self.features = nn.Sequential(
            # Input: 6 channels (3 left + 3 right)
            nn.Conv2d(6, 64, kernel_size=7, stride=2, padding=3),
            nn.BatchNorm2d(64),
            nn.ReLU(inplace=True),
            nn.MaxPool2d(kernel_size=3, stride=2, padding=1),
            
            # Residual blocks
            self._make_residual_block(64, 64, 2),
            self._make_residual_block(64, 128, 2, stride=2),
            self._make_residual_block(128, 256, 2, stride=2),
            self._make_residual_block(256, 512, 2, stride=2),
        )
        
        # Regression head
        self.regressor = nn.Sequential(
            nn.AdaptiveAvgPool2d((1, 1)),
            nn.Flatten(),
            nn.Linear(512, 256),
            nn.ReLU(inplace=True),
            nn.Dropout(0.5),
            nn.Linear(256, num_outputs)
        )
        
        # Initialize weights
        self._initialize_weights()
    
    def _make_residual_block(self, in_channels: int, out_channels: int, num_blocks: int, stride: int = 1):
        layers = []
        
        # First block might need downsampling
        if in_channels != out_channels or stride != 1:
            downsample = nn.Sequential(
                nn.Conv2d(in_channels, out_channels, kernel_size=1, stride=stride, bias=False),
                nn.BatchNorm2d(out_channels)
            )
        else:
            downsample = None
        
        layers.append(ResidualBlock(in_channels, out_channels, stride, downsample))
        
        # Additional blocks
        for _ in range(1, num_blocks):
            layers.append(ResidualBlock(out_channels, out_channels))
            
        return nn.Sequential(*layers)
    
    def _initialize_weights(self):
        for m in self.modules():
            if isinstance(m, nn.Conv2d):
                nn.init.kaiming_normal_(m.weight, mode='fan_out', nonlinearity='relu')
            elif isinstance(m, nn.BatchNorm2d):
                nn.init.constant_(m.weight, 1)
                nn.init.constant_(m.bias, 0)
    
    def forward(self, x: torch.Tensor) -> Dict[str, torch.Tensor]:
        features = self.features(x)
        output = self.regressor(features)
        
        # Split into different outputs
        return {
            'width': output[:, 0],
            'height': output[:, 1],
            'cross_section': output[:, 2],
            'defects': output[:, 3]
        }

class ResidualBlock(nn.Module):
    """Basic residual block with two 3x3 convolutions."""
    
    def __init__(self, in_channels: int, out_channels: int, stride: int = 1, downsample = None):
        super().__init__()
        
        self.conv1 = nn.Conv2d(in_channels, out_channels, kernel_size=3, stride=stride, padding=1, bias=False)
        self.bn1 = nn.BatchNorm2d(out_channels)
        self.conv2 = nn.Conv2d(out_channels, out_channels, kernel_size=3, padding=1, bias=False)
        self.bn2 = nn.BatchNorm2d(out_channels)
        self.downsample = downsample
        self.relu = nn.ReLU(inplace=True)
    
    def forward(self, x: torch.Tensor) -> torch.Tensor:
        identity = x
        
        out = self.conv1(x)
        out = self.bn1(out)
        out = self.relu(out)
        
        out = self.conv2(out)
        out = self.bn2(out)
        
        if self.downsample is not None:
            identity = self.downsample(x)
            
        out += identity
        out = self.relu(out)
        
        return out

def train_model(model, train_loader, val_loader, criterion, optimizer, num_epochs=100, device='cuda'):
    """Train the model with validation."""
    model = model.to(device)
    best_val_loss = float('inf')
    
    train_losses = []
    val_losses = []
    
    for epoch in range(num_epochs):
        # Training phase
        model.train()
        running_loss = 0.0
        
        for inputs, targets in train_loader:
            inputs = inputs.to(device)
            targets = {k: v.to(device) for k, v in targets.items()}
            
            optimizer.zero_grad()
            
            # Forward pass
            outputs = model(inputs)
            
            # Compute loss for each output
            loss = 0
            for key in outputs:
                loss += criterion(outputs[key], targets[key])
            
            # Backward pass and optimize
            loss.backward()
            optimizer.step()
            
            running_loss += loss.item() * inputs.size(0)
        
        epoch_train_loss = running_loss / len(train_loader.dataset)
        train_losses.append(epoch_train_loss)
        
        # Validation phase
        val_loss = evaluate_model(model, val_loader, criterion, device)
        val_losses.append(val_loss)
        
        print(f'Epoch {epoch+1}/{num_epochs} - Train Loss: {epoch_train_loss:.6f}, Val Loss: {val_loss:.6f}')
        
        # Save best model
        if val_loss < best_val_loss:
            best_val_loss = val_loss
            torch.save(model.state_dict(), 'best_model.pth')
    
    # Plot training history
    plt.figure(figsize=(10, 5))
    plt.plot(train_losses, label='Training Loss')
    plt.plot(val_losses, label='Validation Loss')
    plt.title('Training and Validation Loss')
    plt.xlabel('Epoch')
    plt.ylabel('Loss')
    plt.legend()
    plt.savefig('training_history.png')
    
    return model

def evaluate_model(model, data_loader, criterion, device='cuda'):
    """Evaluate the model on the given dataset."""
    model.eval()
    running_loss = 0.0
    
    with torch.no_grad():
        for inputs, targets in data_loader:
            inputs = inputs.to(device)
            targets = {k: v.to(device) for k, v in targets.items()}
            
            # Forward pass
            outputs = model(inputs)
            
            # Compute loss for each output
            loss = 0
            for key in outputs:
                loss += criterion(outputs[key], targets[key])
            
            running_loss += loss.item() * inputs.size(0)
    
    return running_loss / len(data_loader.dataset)

def export_to_onnx(model, input_size=(6, 256, 512), filename='stereo_net.onnx'):
    """Export the model to ONNX format."""
    model.eval()
    dummy_input = torch.randn(1, *input_size).to(next(model.parameters()).device)
    
    # Export the model
    torch.onnx.export(
        model,                      # model being run
        dummy_input,                # model input (or a tuple for multiple inputs)
        filename,                   # where to save the model
        export_params=True,         # store the trained parameter weights
        opset_version=11,           # the ONNX version to export the model to
        do_constant_folding=True,   # whether to execute constant folding
        input_names=['input'],      # model's input names
        output_names=['width', 'height', 'cross_section', 'defects'],
        dynamic_axes={
            'input': {0: 'batch_size'},    # variable length axes
            'width': {0: 'batch_size'},
            'height': {0: 'batch_size'},
            'cross_section': {0: 'batch_size'},
            'defects': {0: 'batch_size'}
        }
    )
    print(f'Model exported to {filename}')

def main():
    # Set random seed for reproducibility
    torch.manual_seed(42)
    np.random.seed(42)
    
    # Set device
    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    print(f'Using device: {device}')
    
    # Create datasets
    train_dataset = StereoBeadDataset('data/bead_dataset', split='train')
    val_dataset = StereoBeadDataset('data/bead_dataset', split='val')
    
    # Create data loaders
    train_loader = DataLoader(
        train_dataset,
        batch_size=16,
        shuffle=True,
        num_workers=4,
        pin_memory=True
    )
    
    val_loader = DataLoader(
        val_dataset,
        batch_size=16,
        shuffle=False,
        num_workers=4,
        pin_memory=True
    )
    
    # Initialize model, loss, and optimizer
    model = StereoNet()
    criterion = nn.MSELoss()
    optimizer = optim.Adam(model.parameters(), lr=1e-4)
    
    # Train the model
    print('Starting training...')
    model = train_model(
        model,
        train_loader,
        val_loader,
        criterion,
        optimizer,
        num_epochs=100,
        device=device
    )
    
    # Export to ONNX
    print('Exporting model to ONNX...')
    export_to_onnx(model)
    
    print('Training complete!')

if __name__ == '__main__':
    main()
