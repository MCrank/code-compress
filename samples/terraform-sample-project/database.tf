resource "aws_db_instance" "main" {
  identifier     = "${local.name_prefix}-db"
  engine         = "postgres"
  engine_version = "15.4"
  instance_class = var.db_instance_class

  allocated_storage     = 20
  max_allocated_storage = 100

  db_name  = "appdb"
  username = "admin"
  password = "changeme"

  vpc_security_group_ids = [aws_security_group.db.id]

  skip_final_snapshot = true

  tags = merge(local.common_tags, {
    Name = "${local.name_prefix}-db"
  })
}

# IAM policy for application access to RDS
resource "aws_iam_policy" "db_access" {
  name        = "${local.name_prefix}-db-access"
  description = "Policy for application access to RDS"

  policy = <<-EOF
    {
      "Version": "2012-10-17",
      "Statement": [
        {
          "Effect": "Allow",
          "Action": [
            "rds-db:connect"
          ],
          "Resource": "${aws_db_instance.main.arn}"
        }
      ]
    }
  EOF

  tags = local.common_tags
}
