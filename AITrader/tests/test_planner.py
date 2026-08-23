from app.planner import MetaPlanner, PlanAction
from app.portfolio import Portfolio

def test_planner_scans_initially():
    p=Portfolio()
    plan=MetaPlanner().decide(p,0,2)
    assert plan.action==PlanAction.SCAN

def test_planner_enters_research_after_streak():
    p=Portfolio()
    plan=MetaPlanner().decide(p,2,2)
    assert plan.action==PlanAction.RESEARCH
