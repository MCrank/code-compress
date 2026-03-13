# Production configuration
project_name      = "webapp"
environment       = "prod"
aws_region        = "us-east-1"
instance_type     = "t3.small"
instance_count    = 3
enable_monitoring = true
db_instance_class = "db.t3.medium"

allowed_cidr_blocks = [
  "10.0.0.0/8",
  "172.16.0.0/12"
]

extra_tags = {
  CostCenter = "engineering"
  Team       = "platform"
}
