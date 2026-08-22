Runtime workflow diagrams are written incrementally here.

File name:
workflow-{WorkflowId}.txt

The file is created as soon as the workflow starts.
Each completed stage appends its actual counts immediately.
Writes are idempotent, so resume does not duplicate already logged stages.
