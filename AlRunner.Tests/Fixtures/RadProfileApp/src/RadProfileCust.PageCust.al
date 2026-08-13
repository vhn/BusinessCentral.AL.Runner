namespace AlRunner.Tests.RadProfileApp;

pagecustomization "RAD Profile Cust" customizes "RAD Profile Card"
{
    layout
    {
        modify(Amount)
        {
            Visible = false;
        }
    }
}
