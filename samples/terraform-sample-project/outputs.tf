output "web_instance_ids" {
  description = "IDs of the web server instances"
  value       = aws_instance.web[*].id
}

output "web_public_ips" {
  description = "Public IPs of the web servers"
  value       = aws_eip.web[*].public_ip
}

output "db_endpoint" {
  description = "RDS database endpoint"
  value       = aws_db_instance.main.endpoint
}

output "db_password" {
  description = "Database password (sensitive)"
  value       = aws_db_instance.main.password
  sensitive   = true
}

output "security_group_ids" {
  value = {
    web = aws_security_group.web.id
    db  = aws_security_group.db.id
  }
}

output "vpc_module_id" {
  description = "VPC ID from the VPC module"
  value       = module.vpc.vpc_id
}
