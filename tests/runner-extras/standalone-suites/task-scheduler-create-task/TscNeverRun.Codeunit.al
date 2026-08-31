codeunit 64302 "TSC Never Run"
{
    // Negative control: any accidental scheduler dispatch makes the lifecycle test fail loudly.
    trigger OnRun()
    begin
        Error('TaskScheduler executed a task that must remain pending.');
    end;
}
