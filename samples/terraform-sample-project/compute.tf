# Find latest Ubuntu AMI
data "aws_ami" "ubuntu" {
  most_recent = true
  owners      = ["099720109477"]

  filter {
    name   = "name"
    values = ["ubuntu/images/hvm-ssd/ubuntu-jammy-22.04-amd64-server-*"]
  }

  filter {
    name   = "virtualization-type"
    values = ["hvm"]
  }
}

# Web server instances
resource "aws_instance" "web" {
  count         = var.instance_count
  ami           = data.aws_ami.ubuntu.id
  instance_type = var.instance_type

  vpc_security_group_ids = [aws_security_group.web.id]

  user_data = <<-EOF
    #!/bin/bash
    apt-get update
    apt-get install -y nginx
    systemctl start nginx
    echo "Server ${count.index}" > /var/www/html/index.html
  EOF

  monitoring = var.enable_monitoring

  lifecycle {
    create_before_destroy = true
    ignore_changes        = [ami]
  }

  tags = merge(local.common_tags, {
    Name = "${local.name_prefix}-web-${count.index}"
    Role = "webserver"
  })
}

# Elastic IPs for web servers
resource "aws_eip" "web" {
  count    = var.instance_count
  instance = aws_instance.web[count.index].id
  domain   = "vpc"

  tags = merge(local.common_tags, {
    Name = "${local.name_prefix}-web-eip-${count.index}"
  })
}
