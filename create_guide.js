const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  Header, Footer, AlignmentType, HeadingLevel, BorderStyle, WidthType,
  ShadingType, PageNumber, PageBreak, LevelFormat
} = require("docx");

const border = { style: BorderStyle.SINGLE, size: 1, color: "CCCCCC" };
const borders = { top: border, bottom: border, left: border, right: border };
const cellMargins = { top: 80, bottom: 80, left: 120, right: 120 };

// Full content width for US Letter with 1" margins
const TABLE_WIDTH = 9360;

function hdrCell(text, width) {
  return new TableCell({
    borders,
    width: { size: width, type: WidthType.DXA },
    shading: { fill: "2F5496", type: ShadingType.CLEAR },
    margins: cellMargins,
    children: [new Paragraph({ children: [new TextRun({ text, bold: true, color: "FFFFFF", font: "Arial", size: 20 })] })]
  });
}

function cell(text, width, opts = {}) {
  return new TableCell({
    borders,
    width: { size: width, type: WidthType.DXA },
    shading: opts.shade ? { fill: opts.shade, type: ShadingType.CLEAR } : undefined,
    margins: cellMargins,
    children: [new Paragraph({
      children: [new TextRun({ text: String(text), font: "Arial", size: 20, bold: opts.bold || false })]
    })]
  });
}

function heading(text, level) {
  return new Paragraph({ heading: level, spacing: { before: 300, after: 150 }, children: [new TextRun({ text, font: "Arial" })] });
}

function para(text, opts = {}) {
  return new Paragraph({
    spacing: { after: 120 },
    children: [new TextRun({ text, font: "Arial", size: 22, bold: opts.bold || false, italics: opts.italic || false })]
  });
}

function multiRun(runs) {
  return new Paragraph({
    spacing: { after: 120 },
    children: runs.map(r => new TextRun({ text: r.text, font: "Arial", size: 22, bold: r.bold || false, italics: r.italic || false }))
  });
}

const doc = new Document({
  styles: {
    default: { document: { run: { font: "Arial", size: 22 } } },
    paragraphStyles: [
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 36, bold: true, font: "Arial", color: "2F5496" },
        paragraph: { spacing: { before: 360, after: 200 }, outlineLevel: 0 } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 28, bold: true, font: "Arial", color: "2F5496" },
        paragraph: { spacing: { before: 240, after: 150 }, outlineLevel: 1 } },
      { id: "Heading3", name: "Heading 3", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 24, bold: true, font: "Arial", color: "4472C4" },
        paragraph: { spacing: { before: 200, after: 100 }, outlineLevel: 2 } },
    ]
  },
  numbering: {
    config: [
      { reference: "bullets", levels: [
        { level: 0, format: LevelFormat.BULLET, text: "\u2022", alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 720, hanging: 360 } } } },
        { level: 1, format: LevelFormat.BULLET, text: "\u25E6", alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 1440, hanging: 360 } } } },
      ]},
      { reference: "numbers", levels: [
        { level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 720, hanging: 360 } } } },
      ]},
      { reference: "steps", levels: [
        { level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 720, hanging: 360 } } } },
      ]},
    ]
  },
  sections: [
    // ── COVER PAGE ────────────────────────────────────────────────────────
    {
      properties: {
        page: {
          size: { width: 12240, height: 15840 },
          margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 }
        }
      },
      children: [
        new Paragraph({ spacing: { before: 3000 } }),
        new Paragraph({
          alignment: AlignmentType.CENTER,
          spacing: { after: 200 },
          children: [new TextRun({ text: "APS", font: "Arial", size: 72, bold: true, color: "2F5496" })]
        }),
        new Paragraph({
          alignment: AlignmentType.CENTER,
          spacing: { after: 100 },
          children: [new TextRun({ text: "Advanced Planning & Scheduling", font: "Arial", size: 36, color: "4472C4" })]
        }),
        new Paragraph({
          alignment: AlignmentType.CENTER,
          spacing: { after: 400 },
          children: [new TextRun({ text: "Steel / Metal Plant", font: "Arial", size: 28, color: "808080" })]
        }),
        new Paragraph({
          alignment: AlignmentType.CENTER,
          spacing: { after: 100 },
          children: [new TextRun({ text: "System Guide", font: "Arial", size: 32, bold: true, color: "2F5496" })]
        }),
        new Paragraph({ spacing: { before: 2000 } }),
        new Paragraph({
          alignment: AlignmentType.CENTER,
          children: [new TextRun({ text: "April 2026", font: "Arial", size: 22, color: "808080" })]
        }),
      ]
    },

    // ── MAIN CONTENT ──────────────────────────────────────────────────────
    {
      properties: {
        page: {
          size: { width: 12240, height: 15840 },
          margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 }
        }
      },
      headers: {
        default: new Header({
          children: [new Paragraph({
            alignment: AlignmentType.RIGHT,
            children: [new TextRun({ text: "APS System Guide", font: "Arial", size: 18, color: "808080", italics: true })]
          })]
        })
      },
      footers: {
        default: new Footer({
          children: [new Paragraph({
            alignment: AlignmentType.CENTER,
            children: [new TextRun({ text: "Page ", font: "Arial", size: 18, color: "808080" }), new TextRun({ children: [PageNumber.CURRENT], font: "Arial", size: 18, color: "808080" })]
          })]
        })
      },
      children: [
        // ── 1. WHAT IS THIS? ──────────────────────────────────────────────
        heading("1. What Is This?", HeadingLevel.HEADING_1),
        para("This is a Finite Scheduling System for an integrated steel plant with Blast Furnace, Steel Melt Shop (2 EAF + 3 LRF + 1 VD + 2 CCM), and 2 Rolling Mills. It takes your sales orders, groups them into campaigns, figures out what raw materials and capacity you need, checks material availability, and generates a constrained production schedule that tells each machine exactly what to produce and when."),
        para("The system lives in an Excel workbook connected to a Python engine. You interact with Excel (view data, click buttons). Python does the heavy computation (campaign building, BOM explosion, capacity analysis, finite scheduling) and writes results back into Excel."),
        para("Current runtime stack: pandas for data shaping, xlwings + pywin32 for Excel connectivity, openpyxl for workbook generation, and OR-Tools CP-SAT for finite scheduling. The plant configuration models 11 resources with a 50 MT EAF heat size."),

        // Architecture diagram as a table
        new Paragraph({ spacing: { before: 200, after: 100 }, children: [new TextRun({ text: "How It Works:", font: "Arial", size: 22, bold: true })] }),

        new Table({
          width: { size: TABLE_WIDTH, type: WidthType.DXA },
          columnWidths: [1872, 1872, 1872, 1872, 1872],
          rows: [
            new TableRow({ children: [
              new TableCell({ borders, width: { size: 1872, type: WidthType.DXA }, shading: { fill: "D5E8F0", type: ShadingType.CLEAR }, margins: cellMargins,
                children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [new TextRun({ text: "Sales Orders", font: "Arial", size: 18, bold: true })] })] }),
              new TableCell({ borders, width: { size: 1872, type: WidthType.DXA }, shading: { fill: "D5E8F0", type: ShadingType.CLEAR }, margins: cellMargins,
                children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [new TextRun({ text: "BOM Explosion", font: "Arial", size: 18, bold: true })] })] }),
              new TableCell({ borders, width: { size: 1872, type: WidthType.DXA }, shading: { fill: "D5E8F0", type: ShadingType.CLEAR }, margins: cellMargins,
                children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [new TextRun({ text: "Capacity Map", font: "Arial", size: 18, bold: true })] })] }),
              new TableCell({ borders, width: { size: 1872, type: WidthType.DXA }, shading: { fill: "D5E8F0", type: ShadingType.CLEAR }, margins: cellMargins,
                children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [new TextRun({ text: "Scheduler", font: "Arial", size: 18, bold: true })] })] }),
              new TableCell({ borders, width: { size: 1872, type: WidthType.DXA }, shading: { fill: "D5E8F0", type: ShadingType.CLEAR }, margins: cellMargins,
                children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [new TextRun({ text: "Schedule Output", font: "Arial", size: 18, bold: true })] })] }),
            ]}),
            new TableRow({ children: [
              cell("What do customers want?", 1872),
              cell("What materials do we need?", 1872),
              cell("Can our machines handle it?", 1872),
              cell("What runs where & when?", 1872),
              cell("Final production plan", 1872),
            ]}),
          ]
        }),

        // ── 2. FILE STRUCTURE ──────────────────────────────────────────────
        new Paragraph({ children: [new PageBreak()] }),
        heading("2. File Structure", HeadingLevel.HEADING_1),
        para("All files live in a single folder. Here is what each file does:"),

        new Table({
          width: { size: TABLE_WIDTH, type: WidthType.DXA },
          columnWidths: [3500, 5860],
          rows: [
            new TableRow({ children: [hdrCell("File / Folder", 3500), hdrCell("Purpose", 5860)] }),
            new TableRow({ children: [cell("APS_BF_SMS_RM.xlsm", 3500, { bold: true }), cell("The Excel workbook. All data input, buttons, and output sheets.", 5860)] }),
            new TableRow({ children: [cell("archive/legacy_single_workbook_path/", 3500, { bold: true }), cell("Retired APS_Steel_Template workbook flow and old root-level scripts. Kept only for reference.", 5860)] }),
            new TableRow({ children: [cell("aps_functions.py", 3500, { bold: true }), cell("Bridge between Excel buttons and Python engine (xlwings).", 5860)] }),
            new TableRow({ children: [cell("run_all.py", 3500, { bold: true }), cell("Run full pipeline from command line: python run_all.py", 5860)] }),
            new TableRow({ children: [cell("engine/bom_explosion.py", 3500, { bold: true }), cell("Explodes multi-level BOM, nets against inventory.", 5860)] }),
            new TableRow({ children: [cell("engine/capacity.py", 3500, { bold: true }), cell("Maps demand to machine hours, compares vs available capacity.", 5860)] }),
            new TableRow({ children: [cell("engine/campaign.py", 3500, { bold: true }), cell("Campaign builder: groups SOs into release buckets, consumes FG stock, checks material availability via BOM.", 5860)] }),
            new TableRow({ children: [cell("engine/scheduler.py", 3500, { bold: true }), cell("Finite scheduler using OR-Tools CP-SAT, with greedy fallback only if the solver cannot produce a usable schedule.", 5860)] }),
            new TableRow({ children: [cell("scenarios/scenario_runner.py", 3500, { bold: true }), cell("Runs 4 base what-if scenarios and adds extra stress cases when yield loss, rush order, or overtime controls are populated.", 5860)] }),
            new TableRow({ children: [cell("data/loader.py", 3500, { bold: true }), cell("Reads Excel file into pandas DataFrames for standalone testing.", 5860)] }),
            new TableRow({ children: [cell("setup_excel.py", 3500, { bold: true }), cell("One-time setup: embeds VBA + creates buttons in workbook.", 5860)] }),
            new TableRow({ children: [cell("build_template.py", 3500, { bold: true }), cell("One-time: generates the canonical APS_BF_SMS_RM workbook with dummy steel data.", 5860)] }),
          ]
        }),

        para("Only one workbook flow is supported: APS_BF_SMS_RM.xlsm. Older APS_Steel_Template files and their root-level scripts are archived under archive/legacy_single_workbook_path and are not part of the live runtime."),

        // ── 3. EXCEL SHEETS ────────────────────────────────────────────────
        heading("3. Excel Sheets Explained", HeadingLevel.HEADING_1),

        heading("Input Sheets (you fill these)", HeadingLevel.HEADING_2),

        new Table({
          width: { size: TABLE_WIDTH, type: WidthType.DXA },
          columnWidths: [2400, 3500, 3460],
          rows: [
            new TableRow({ children: [hdrCell("Sheet", 2400), hdrCell("What It Contains", 3500), hdrCell("Key Columns", 3460)] }),
            new TableRow({ children: [cell("Help", 2400, { bold: true }), cell("Dedicated workbook reference tab. Inline sheet guides are removed from the working tabs.", 3500), cell("Sheet, Type, What It Is For, How To Use It", 3460)] }),
            new TableRow({ children: [cell("SKU_Master", 2400, { bold: true }), cell("Finished goods, RM outputs, billets, EAF/LRF/VD/BF intermediates, raw materials, and byproduct/waste SKUs", 3500), cell("SKU_ID, Category, Grade, Section_mm, Needs_VD", 3460)] }),
            new TableRow({ children: [cell("BOM", 2400, { bold: true }), cell("Stagewise Bill of Materials with input and byproduct rows from FG down to BF/raw materials", 3500), cell("Parent_SKU, Child_SKU, Flow_Type, Qty_Per, Scrap_%", 3460)] }),
            new TableRow({ children: [cell("Inventory", 2400, { bold: true }), cell("Current stock by SKU and location, including billets, hot metal, WIP stages, and optional waste sinks", 3500), cell("SKU_ID, Available_Qty, Reserved_Qty", 3460)] }),
            new TableRow({ children: [cell("Sales_Orders", 2400, { bold: true }), cell("Customer orders with quantities and delivery dates", 3500), cell("SO_ID, SKU_ID, Order_Qty, Delivery_Date", 3460)] }),
            new TableRow({ children: [cell("Resource_Master", 2400, { bold: true }), cell("Machines: EAF, CCM, Rolling Mill, etc.", 3500), cell("Resource_ID, Avail_Hours_Day, Efficiency_%", 3460)] }),
            new TableRow({ children: [cell("Routing", 2400, { bold: true }), cell("How each SKU flows through machines (operation sequence)", 3500), cell("SKU_ID, Operation_Seq, Resource_ID, Cycle_Time", 3460)] }),
            new TableRow({ children: [cell("Changeover_Matrix", 2400, { bold: true }), cell("Time (minutes) to switch between products on a machine", 3500), cell("From SKU x To SKU matrix", 3460)] }),
            new TableRow({ children: [cell("Scenarios", 2400, { bold: true }), cell("Active planning controls plus what-if parameters", 3500), cell("Demand Spike, Machine Down, Start Hour, Solver Limit, Yield Loss", 3460)] }),
          ]
        }),

        para("The workbook now uses one dedicated Help sheet instead of right-side inline Sheet Guide panels on every working tab. This keeps the planning sheets visually cleaner."),

        heading("Output Sheets (Python fills these)", HeadingLevel.HEADING_2),

        new Table({
          width: { size: TABLE_WIDTH, type: WidthType.DXA },
          columnWidths: [2800, 6560],
          rows: [
            new TableRow({ children: [hdrCell("Sheet", 2800), hdrCell("What Gets Written", 6560)] }),
            new TableRow({ children: [cell("BOM_Output", 2800, { bold: true }), cell("BOM explosion results: gross requirements, available inventory, and net requirements per material", 6560)] }),
            new TableRow({ children: [cell("Capacity_Map", 2800, { bold: true }), cell("Demand hours vs available hours per machine. Shows utilisation %, flags overloaded/underutilised.", 6560)] }),
            new TableRow({ children: [cell("Schedule_Output", 2800, { bold: true }), cell("Detailed dispatch schedule: one row per planned operation on one resource with start/end time, heat no, quantity, and status.", 6560)] }),
            new TableRow({ children: [cell("Campaign_Schedule", 2800, { bold: true }), cell("Campaign-level summary across EAF, CCM, and RM stages for planner and management review.", 6560)] }),
            new TableRow({ children: [cell("Material_Plan", 2800, { bold: true }), cell("Campaign-by-campaign material commitment trace showing what inventory was consumed, what remains, and what caused any hold.", 6560)] }),
            new TableRow({ children: [cell("Equipment_Schedule", 2800, { bold: true }), cell("Separate dispatch tables per equipment with blank rows between sections for cleaner operational handoff.", 6560)] }),
            new TableRow({ children: [cell("Schedule_Gantt", 2800, { bold: true }), cell("Resource swim-lane Gantt with adaptive 1/2/4-hour campaign bars, plant-separated lanes, and utilisation summary per machine.", 6560)] }),
            new TableRow({ children: [cell("Scenario_Output", 2800, { bold: true }), cell("Base scenario comparison plus optional stress cases, showing service, lateness, throughput, bottleneck, and utilisation KPIs.", 6560)] }),
            new TableRow({ children: [cell("KPI_Dashboard", 2800, { bold: true }), cell("Chart-based APS dashboard: KPI tiles plus 6 charts for utilisation, campaign outcome, operation mix, throughput, and scenario comparison.", 6560)] }),
            new TableRow({ children: [cell("Theo_vs_Actual", 2800, { bold: true }), cell("Placeholder: compare planned vs actual process times once production data flows in.", 6560)] }),
          ]
        }),

        // ── 4. HOW SCHEDULING WORKS ────────────────────────────────────────
        new Paragraph({ children: [new PageBreak()] }),
        heading("4. How Scheduling Works", HeadingLevel.HEADING_1),

        heading("Step-by-Step Pipeline", HeadingLevel.HEADING_2),

        multiRun([{ text: "Step 1: Read Inputs and Scenario Controls", bold: true }]),
        para("The APS engine reads sales orders, inventory, BOM, resources, routing, and scenario-control values from Excel. Demand spike, downtime hours and start offset, planning horizon, solver time limit, yield loss, rush order, and overtime are applied before the scheduling model is built."),

        multiRun([{ text: "Step 2: Normalize Open Demand and Consume FG Stock", bold: true }]),
        para("Only open sales orders are considered. Finished-good inventory is consumed first so only uncovered make quantity becomes production demand. This keeps the schedule focused on what the plant still needs to produce."),

        multiRun([{ text: "Step 3: Build Compatible Campaigns", bold: true }]),
        para("The current campaign engine groups compatible orders into one PPC release batch based on Campaign Group, Grade, Billet Family, and VD requirement. The batch is then split by campaign-size rules. Each campaign contains linked RM production orders derived from the covered sales orders."),

        multiRun([{ text: "Step 4: Material Release Check", bold: true }]),
        para("Each campaign is checked against BOM and inventory before it is released to scheduling. If raw materials or intermediates are short, the campaign is marked MATERIAL HOLD and does not enter the finite schedule."),

        multiRun([{ text: "Step 5: Build the Finite Scheduling Model", bold: true }]),
        para("For released campaigns, the engine builds SMS heat tasks and RM production-order tasks. Routing times are used where available, and running rows that planners marked as RUNNING are frozen so their timing and resource assignment stay pinned on rerun."),

        multiRun([{ text: "Step 6: Apply Process Precedence", bold: true }]),
        para("Within each campaign, SMS precedence is modeled as EAF -> LRF -> VD? -> CCM. RM production orders are then allowed to start only after the campaign's CCM work is complete, and the solver can choose among alternate machines in each resource group."),

        multiRun([{ text: "Step 7: Enforce Campaign Serialization", bold: true }]),
        para("Campaigns are treated as PPC release batches, not mix-and-match machine buckets. Campaign n+1 cannot start until campaign n is fully complete end-to-end. That means the plant should not switch to the next campaign on CCM, RM, or any other modeled unit before the current campaign is finished."),

        multiRun([{ text: "Step 8: Apply Machine and Downtime Constraints", bold: true }]),
        para("The model enforces no-overlap on each machine calendar, inserts a blocking interval for the selected machine-down resource at the configured downtime start hour, and applies partial RM changeover gaps from the Changeover_Matrix. Steel flow is planned back-to-back without an inserted artificial safety buffer."),

        multiRun([{ text: "Step 9: Solve and Fallback", bold: true }]),
        para("The OR-Tools CP-SAT model is solved with a configurable time limit and lateness pressure on both SMS completion and RM completion. If the exact model becomes infeasible, the engine falls back to the greedy scheduler so the workbook still receives a usable plan."),

        multiRun([{ text: "Step 10: Write Outputs and Dashboards", bold: true }]),
        para("The run writes machine-level rows to Schedule_Output, campaign rows to Campaign_Schedule, material commitments to Material_Plan, grouped packets to Equipment_Schedule, a resource swim-lane view to Schedule_Gantt, and charts plus KPI tiles to KPI_Dashboard."),

        heading("Campaign Building Logic", HeadingLevel.HEADING_2),
        para("The campaign engine (engine/campaign.py) is central to the APS. Campaigns are the unit of PPC release and execution — every scheduling decision flows through them:"),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          children: [new TextRun({ text: "Open SOs are prioritized by Priority (URGENT > HIGH > NORMAL > LOW), then Delivery_Date, then Order_Date.", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          children: [new TextRun({ text: "Finished-good inventory is consumed first so only uncovered make quantity becomes production demand.", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          children: [new TextRun({ text: "Compatible SOs are grouped by Route Family = Campaign_Group + Grade + Billet_Family + VD requirement.", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          children: [new TextRun({ text: "Groups exceeding Max Campaign MT (default 500) are split. Below-minimum campaigns are flagged.", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          children: [new TextRun({ text: "Each campaign generates derived production orders for the RM. Heats are calculated as ceil(total_liquid_MT / 50).", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          children: [new TextRun({ text: "Released campaigns are serialized. The next campaign cannot begin until the previous campaign is fully complete end-to-end.", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          spacing: { after: 120 },
          children: [new TextRun({ text: "Material availability is checked via BOM explosion per campaign. Shortages result in MATERIAL HOLD status.", font: "Arial", size: 22 })]
        }),

        heading("Important Note on Constraint Evaluation", HeadingLevel.HEADING_2),
        para("The planning pipeline above happens in a sequence. The solver itself does not evaluate constraints one-by-one in that same sequence. Once the CP-SAT model is built, active constraints are solved together as one optimization problem."),
        para("So the row order in Schedule_Output is a display order for planners, not the order in which constraints were checked by the solver."),

        // ── 5. HOW TO USE ──────────────────────────────────────────────────
        heading("5. How to Use the System", HeadingLevel.HEADING_1),

        heading("Option A: Click Buttons in Excel", HeadingLevel.HEADING_2),
        para("Open APS_BF_SMS_RM.xlsm. You can launch actions from the Control_Panel or from the relevant working sheets such as BOM, Scenarios, Capacity_Map, Schedule_Output, Campaign_Schedule, Material_Plan, Equipment_Schedule, Schedule_Gantt, BOM_Output, and Scenario_Output."),

        new Table({
          width: { size: TABLE_WIDTH, type: WidthType.DXA },
          columnWidths: [3200, 6160],
          rows: [
            new TableRow({ children: [hdrCell("Button", 3200), hdrCell("What It Does", 6160)] }),
            new TableRow({ children: [cell("Run BOM Explosion", 3200, { bold: true }), cell("Explodes demand through BOM, nets against inventory, writes to BOM_Output", 6160)] }),
            new TableRow({ children: [cell("Run Capacity Map", 3200, { bold: true }), cell("Calculates machine hours needed vs available, writes to Capacity_Map sheet", 6160)] }),
            new TableRow({ children: [cell("Run Schedule", 3200, { bold: true }), cell("Generates the live production schedule, writes to Schedule_Output, Campaign_Schedule, Material_Plan, Equipment_Schedule, and Schedule_Gantt", 6160)] }),
            new TableRow({ children: [cell("Run Scenarios", 3200, { bold: true }), cell("Runs 4 base scenarios plus optional extra stress cases, writes comparison to Scenario_Output", 6160)] }),
            new TableRow({ children: [cell("Clear All Outputs", 3200, { bold: true }), cell("Clears all computed results from output sheets", 6160)] }),
          ]
        }),

        heading("Option B: Run from Command Line", HeadingLevel.HEADING_2),
        para("With the Excel file open, run from a terminal:"),
        new Paragraph({
          spacing: { after: 60 },
          shading: { fill: "F2F2F2", type: ShadingType.CLEAR },
          children: [new TextRun({ text: "  python run_all.py", font: "Consolas", size: 20 })]
        }),
        new Paragraph({
          spacing: { after: 60 },
          shading: { fill: "F2F2F2", type: ShadingType.CLEAR },
          children: [new TextRun({ text: "  python run_all.py bom", font: "Consolas", size: 20 })]
        }),
        new Paragraph({
          spacing: { after: 60 },
          shading: { fill: "F2F2F2", type: ShadingType.CLEAR },
          children: [new TextRun({ text: "  python run_all.py capacity", font: "Consolas", size: 20 })]
        }),
        new Paragraph({
          spacing: { after: 60 },
          shading: { fill: "F2F2F2", type: ShadingType.CLEAR },
          children: [new TextRun({ text: "  python run_all.py schedule", font: "Consolas", size: 20 })]
        }),
        new Paragraph({
          spacing: { after: 120 },
          shading: { fill: "F2F2F2", type: ShadingType.CLEAR },
          children: [new TextRun({ text: "  python run_all.py scenarios", font: "Consolas", size: 20 })]
        }),
        para("This connects to the open Excel workbook via xlwings and writes results directly into the sheets."),
        para("Do not run the buttons from any archived APS_Steel_Template workbook. Those files are retained only for historical reference."),

        // ── 6. WHAT-IF SCENARIOS ───────────────────────────────────────────
        heading("6. What-If Scenarios", HeadingLevel.HEADING_1),
        para("The system always runs 4 base scenarios to stress-test your plan, and it can add extra stress cases when yield loss, rush order, or overtime controls are populated:"),
        para("The editable values on the Scenarios sheet also affect the live APS plan when you rerun BOM, Capacity, or Schedule."),

        new Table({
          width: { size: TABLE_WIDTH, type: WidthType.DXA },
          columnWidths: [3200, 6160],
          rows: [
            new TableRow({ children: [hdrCell("Scenario", 3200), hdrCell("What Changes", 6160)] }),
            new TableRow({ children: [cell("Baseline", 3200, { bold: true }), cell("Current demand and full machine availability. Your reference point.", 6160)] }),
            new TableRow({ children: [cell("Demand +15%", 3200, { bold: true }), cell("All order quantities increased by 15%. Shows which machines break first.", 6160)] }),
            new TableRow({ children: [cell("EAF-01 Down 8hrs", 3200, { bold: true }), cell("Electric Arc Furnace 1 loses 8 hours. Tests bottleneck impact.", 6160)] }),
            new TableRow({ children: [cell("Demand +15% + EAF Down", 3200, { bold: true }), cell("Both stress factors combined. Worst-case planning.", 6160)] }),
            new TableRow({ children: [cell("Yield Loss", 3200, { bold: true }), cell("Appears when Yield Loss (%) is non-zero. Increases required liquid steel and heats.", 6160)] }),
            new TableRow({ children: [cell("Rush Order", 3200, { bold: true }), cell("Appears when Rush Order MT is non-zero. Injects urgent demand into the planning pool.", 6160)] }),
            new TableRow({ children: [cell("Extra Shift", 3200, { bold: true }), cell("Appears when Extra Shift Hours is non-zero. Adds overtime hours to all resources.", 6160)] }),
          ]
        }),

        para("You can modify scenario parameters in the Scenarios sheet before running. Important controls include Demand Spike, Machine Down Hours and Start, Solver Time Limit, Yield Loss, Rush Order MT, Extra Shift Hours, and campaign-size limits. If you manually set a row in Schedule_Output to RUNNING, reruns preserve that row instead of rewriting it."),

        // ── 7. DUMMY DATA ──────────────────────────────────────────────────
        heading("7. Dummy Data (Steel Context)", HeadingLevel.HEADING_1),
        para("The template comes pre-loaded with realistic steel plant data:"),

        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          children: [new TextRun({ text: "21 Finished Goods: Wire Rod Coils across 8 grades (SAE 1008/1018/1035/1045/1065/1080, CHQ 1006, Cr-Mo 4140) and 5 sections (5.5/6.5/8.0/10.0/12.0 mm)", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          children: [new TextRun({ text: "Stage intermediates: RM outputs, CCM billets, EAF liquid steel, LRF output, VD output for selected grades, BF hot metal, and BF raw-mix burden", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          children: [new TextRun({ text: "Tracked byproducts and waste: BF slag, EAF slag, LRF waste, VD waste, CCM crop, RM end cuts, and RM scale", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          children: [new TextRun({ text: "11 Machines: 1 BF, 2 EAFs, 3 LRFs, 1 VD, 2 CCMs, 2 Rolling Mills — EAF heat = 50 MT", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          children: [new TextRun({ text: "30 Sales Orders from Indian wire/steel companies (Tata Wiron, Usha Martin, JSW Steel, etc.)", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "bullets", level: 0 },
          spacing: { after: 120 },
          children: [new TextRun({ text: "Stagewise BOM: FG -> RM Output -> CCM Billet -> LRF/VD Output -> EAF Output -> EAF Charge -> BF Hot Metal / Raw Materials, with byproducts tagged separately", font: "Arial", size: 22 })]
        }),

        para("Replace this data with your actual plant data when ready. The structure and column names must remain the same."),

        // ── 8. PREREQUISITES ───────────────────────────────────────────────
        new Paragraph({ children: [new PageBreak()] }),
        heading("8. Prerequisites & Setup", HeadingLevel.HEADING_1),

        heading("Software Required", HeadingLevel.HEADING_2),
        new Table({
          width: { size: TABLE_WIDTH, type: WidthType.DXA },
          columnWidths: [2800, 3200, 3360],
          rows: [
            new TableRow({ children: [hdrCell("Software", 2800), hdrCell("Version", 3200), hdrCell("Purpose", 3360)] }),
            new TableRow({ children: [cell("Python", 2800, { bold: true }), cell("3.11+ (Windows)", 3200), cell("Runs the scheduling engine", 3360)] }),
            new TableRow({ children: [cell("Excel", 2800, { bold: true }), cell("2016+ or 365", 3200), cell("User interface and data entry", 3360)] }),
            new TableRow({ children: [cell("xlwings", 2800, { bold: true }), cell("pip install xlwings", 3200), cell("Excel-Python bridge", 3360)] }),
            new TableRow({ children: [cell("pywin32", 2800, { bold: true }), cell("pip install pywin32", 3200), cell("Windows COM bridge used by setup and button wiring", 3360)] }),
            new TableRow({ children: [cell("pandas", 2800, { bold: true }), cell("pip install pandas", 3200), cell("Data manipulation", 3360)] }),
            new TableRow({ children: [cell("openpyxl", 2800, { bold: true }), cell("pip install openpyxl", 3200), cell("Excel file generation", 3360)] }),
            new TableRow({ children: [cell("ortools", 2800, { bold: true }), cell("pip install ortools", 3200), cell("Finite scheduling optimizer", 3360)] }),
          ]
        }),

        heading("One-Time Setup Steps", HeadingLevel.HEADING_2),
        new Paragraph({
          numbering: { reference: "steps", level: 0 },
          children: [new TextRun({ text: "Install Python packages: pip install xlwings pywin32 pandas openpyxl ortools", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "steps", level: 0 },
          children: [new TextRun({ text: "Install xlwings Excel add-in: xlwings addin install", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "steps", level: 0 },
          children: [new TextRun({ text: "In Excel: File > Options > Trust Center > Trust Center Settings > Macro Settings > enable \"Trust access to the VBA project object model\"", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "steps", level: 0 },
          children: [new TextRun({ text: "Run: python setup_excel.py (embeds VBA and creates buttons)", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "steps", level: 0 },
          children: [new TextRun({ text: "In xlwings ribbon: set Interpreter to your python.exe path, set PYTHONPATH to the APS folder", font: "Arial", size: 22 })]
        }),
        new Paragraph({
          numbering: { reference: "steps", level: 0 },
          spacing: { after: 120 },
          children: [new TextRun({ text: "Click any button on Control_Panel to verify everything works", font: "Arial", size: 22 })]
        }),

        // ── 9. TECHNICAL GAPS ──────────────────────────────────────────────
        heading("9. Technical Gaps & Next Steps", HeadingLevel.HEADING_1),

        new Table({
          width: { size: TABLE_WIDTH, type: WidthType.DXA },
          columnWidths: [800, 3200, 5360],
          rows: [
            new TableRow({ children: [hdrCell("#", 800), hdrCell("Gap", 3200), hdrCell("Why It Matters / Next Upgrade", 5360)] }),
            new TableRow({ children: [cell("1", 800), cell("Machine-choice objective quality", 3200, { bold: true }), cell("Alternate-machine assignment is active, but the objective still needs stronger load balancing, anti-fragmentation, and symmetry-breaking across parallel units.", 5360)] }),
            new TableRow({ children: [cell("2", 800), cell("Changeover optimization", 3200, { bold: true }), cell("RM same-machine changeovers are active, but full steel sequencing, caster-aware changeovers, and richer metallurgical sequence quality are still simplified.", 5360)] }),
            new TableRow({ children: [cell("3", 800), cell("True steel process continuity", 3200, { bold: true }), cell("CCM continuity, tundish changes, strand-level sequencing, and upstream BF constraints are still simplified.", 5360)] }),
            new TableRow({ children: [cell("4", 800), cell("Planner-facing campaign governance", 3200, { bold: true }), cell("Campaigns are auto-built compatible release buckets today. A stronger APS should support planner approval, campaign boards, and explicit per-SO allocated tonnage.", 5360)] }),
            new TableRow({ children: [cell("5", 800), cell("Calendars and maintenance", 3200, { bold: true }), cell("The model supports available hours and a simple downtime event, but not full shift calendars, crew calendars, or maintenance calendars.", 5360)] }),
            new TableRow({ children: [cell("6", 800), cell("ERP/MES integration", 3200, { bold: true }), cell("Production orders and execution feedback still live mainly in Excel. A stronger APS needs an external system of record and interface layer.", 5360)] }),
            new TableRow({ children: [cell("7", 800), cell("Actual-vs-plan feedback", 3200, { bold: true }), cell("Theo_vs_Actual is ready as a placeholder, but the system still needs actual shop-floor feedback loops for closed-loop planning.", 5360)] }),
            new TableRow({ children: [cell("8", 800), cell("Validation and observability", 3200, { bold: true }), cell("The project still needs stronger tests, schema validation, and run logging to be fully production-grade.", 5360)] }),
          ]
        }),

        // ── 10. CONSTRAINTS ────────────────────────────────────────────────
        heading("10. Scheduling Constraints (Current & Planned)", HeadingLevel.HEADING_1),

        new Table({
          width: { size: TABLE_WIDTH, type: WidthType.DXA },
          columnWidths: [3800, 2800, 2760],
          rows: [
            new TableRow({ children: [hdrCell("Constraint", 3800), hdrCell("Status", 2800), hdrCell("How It Works", 2760)] }),
            new TableRow({ children: [cell("Routing-based durations", 3800), cell("Active", 2800, { shade: "E2EFDA" }), cell("Uses Routing timings where available, with defaults as fallback", 2760)] }),
            new TableRow({ children: [cell("Due-date pressure", 3800), cell("Active", 2800, { shade: "E2EFDA" }), cell("Lateness is tracked and the solver tries to keep campaigns on time", 2760)] }),
            new TableRow({ children: [cell("No machine overlap", 3800), cell("Active", 2800, { shade: "E2EFDA" }), cell("One task per modeled machine at a time", 2760)] }),
            new TableRow({ children: [cell("Alternate-machine assignment", 3800), cell("Active", 2800, { shade: "E2EFDA" }), cell("Optional intervals let the solver choose one machine from each resource pool", 2760)] }),
            new TableRow({ children: [cell("Artificial buffer between jobs", 3800), cell("Removed", 2800, { shade: "FCE4D6" }), cell("Steel flow is planned back-to-back without an inserted safety gap", 2760)] }),
            new TableRow({ children: [cell("Late job detection", 3800), cell("Active", 2800, { shade: "E2EFDA" }), cell("Campaigns finishing after due date are marked LATE", 2760)] }),
            new TableRow({ children: [cell("Operation precedence", 3800), cell("Active", 2800, { shade: "E2EFDA" }), cell("EAF before LRF before VD before CCM; RM starts only after CCM completion", 2760)] }),
            new TableRow({ children: [cell("Machine down interval", 3800), cell("Active", 2800, { shade: "E2EFDA" }), cell("Selected downtime resource is blocked for the configured outage duration starting at the chosen offset", 2760)] }),
            new TableRow({ children: [cell("Running-order freeze", 3800), cell("Active", 2800, { shade: "E2EFDA" }), cell("Rows marked RUNNING are pinned and not rescheduled on rerun", 2760)] }),
            new TableRow({ children: [cell("Material release gate", 3800), cell("Active", 2800, { shade: "E2EFDA" }), cell("Campaigns with shortages are held before they enter finite scheduling", 2760)] }),
            new TableRow({ children: [cell("Changeover duration", 3800), cell("Partial", 2800, { shade: "FFF2CC" }), cell("RM same-machine gaps use the Changeover_Matrix, but full steel sequencing is still simplified", 2760)] }),
            new TableRow({ children: [cell("Customer grouping", 3800), cell("Planned", 2800, { shade: "FFF2CC" }), cell("Ship all orders for same customer together", 2760)] }),
            new TableRow({ children: [cell("Region optimization", 3800), cell("Planned", 2800, { shade: "FFF2CC" }), cell("Group by delivery location/region", 2760)] }),
            new TableRow({ children: [cell("Machine-choice load balancing", 3800), cell("Planned", 2800, { shade: "FFF2CC" }), cell("Needed to balance work more intentionally across parallel units once assignment is already active", 2760)] }),
          ]
        }),
      ]
    }
  ]
});

Packer.toBuffer(doc).then(buffer => {
  const target = path.join(__dirname, "APS_System_Guide.docx");
  try {
    fs.writeFileSync(target, buffer);
    console.log("APS_System_Guide.docx created successfully.");
  } catch (err) {
    const fallback = path.join(__dirname, "APS_System_Guide.updated.docx");
    fs.writeFileSync(fallback, buffer);
    console.log(`APS_System_Guide.docx was locked; wrote updated guide to ${path.basename(fallback)} instead.`);
  }
});
