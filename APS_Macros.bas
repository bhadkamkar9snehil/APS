
Sub RunBOMExplosion()
    RunPython "import aps_functions; aps_functions.run_bom_explosion()"
End Sub

Sub RunCapacityMap()
    RunPython "import aps_functions; aps_functions.run_capacity_map()"
End Sub

Sub RunSchedule()
    RunPython "import aps_functions; aps_functions.run_schedule()"
End Sub

Sub RunScenarios()
    RunPython "import aps_functions; aps_functions.run_scenario()"
End Sub

Sub RunCTP()
    RunPython "import aps_functions; aps_functions.run_ctp()"
End Sub

Sub ClearOutputs()
    RunPython "import aps_functions; aps_functions.clear_outputs()"
End Sub

Sub GoToHelpSheet()
    Worksheets("Help").Activate
    Range("A1").Select
End Sub

Sub GoToControlPanel()
    Worksheets("Control_Panel").Activate
    Range("A1").Select
End Sub
