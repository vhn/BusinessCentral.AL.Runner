controladdin "RAD Idless Addin"
{
    Scripts = 'idless-addin.js';

    // A member, so the add-in has a surface the module definition actually serializes:
    // `Scripts` is a build-time asset list and does not reach ControlAddInDefinition, so an
    // edit to it cannot tell a correctly merged baseline from a stale one.
    procedure Ping();
}
