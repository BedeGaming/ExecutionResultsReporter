﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Runtime.InteropServices;
using ExecutionResultsReporter.TestRail.TestRailObj;
using log4net;
using Newtonsoft.Json;

namespace ExecutionResultsReporter.TestRail
{
    public class TestRailReporter : IReporter
    {
        private readonly ILog _log = LogManager.GetLogger("TestRailReporter");
        private readonly List<KeyValuePair<string, string>> _data;
        private ApiClient _apiClient;
        private readonly int _projectId;
        private List<TestSute> _allSuites;
        private List<TestCase> _allTestCases;
        private List<ConfigurationGroup> _allConfigurations;

        public TestRailReporter(int projectId)
        {
            _projectId = projectId;
            _allSuites = new List<TestSute>();
            _allTestCases = new List<TestCase>();
        }

        public TestRailReporter(int projectId, List<KeyValuePair<string, string>> data)
        {
            _projectId = projectId;
            _allSuites = new List<TestSute>();
            _allTestCases = new List<TestCase>();
            _data = data;
        }

        private void CreateApiClient()
        {
            _log.Debug("Trying to create new test rail api client!");
            if (ConfigurationManager.AppSettings.AllKeys.Contains("TestRail.url") | ConfigurationManager.AppSettings.AllKeys.Contains("TestRail.username") | ConfigurationManager.AppSettings.AllKeys.Contains("TestRail.password"))
            {
                _apiClient = new ApiClient(ConfigurationManager.AppSettings["TestRail.url"])
                {
                    User = ConfigurationManager.AppSettings["TestRail.username"],
                    Password = ConfigurationManager.AppSettings["TestRail.password"]
                };
                _log.Debug("Creation of api client with test rail url '" + ConfigurationManager.AppSettings["TestRail.url"] + "', test rail user name '" + ConfigurationManager.AppSettings["TestRail.username"] + "' and test rail password '" + ConfigurationManager.AppSettings["TestRail.password"] + "' successful.");
            }
            else
            {
                _log.Error("Creation of test rail client failed!");
                throw new Exception("Error, it seems that test rail url, test rail password or test rail user name in app.config under 'TestRail.url', 'TestRail.username' or 'TestRail.password' property is not present!");
            }
        }

        public void SetApiClient(ApiClient client)
        {
            _apiClient = client;
        }

        public void LoadAllSuitesForProject()
        {
            _allSuites.Clear();
            _log.Debug("Extracting all suites from project with id: " + _projectId);
            var suites = JsonConvert.DeserializeObject<List<TestSute>>(_apiClient.SendGet("get_suites/" + _projectId).ToString());
            _log.Debug(suites.Count + " suets retrieved.");
            _allSuites = suites.ToList();
        }

        public TestSute FindSuiteByName(string suiteName)
        {
            if (_allSuites.Any())
            {
                foreach (var testRailTestSuteObj in _allSuites.Where(testRailTestSuteObj => testRailTestSuteObj.name == suiteName))
                {
                    _log.Info("Suite with name '" + testRailTestSuteObj.name + "' and id '" + testRailTestSuteObj.id + "' found.");
                    return testRailTestSuteObj;
                }
            }
            _log.Info("There a no suites loaded.");
            return null;
        }

        public TestSute CreateNewSuiteIfNotExist(string suiteName)
        {
            TestSute newSuite;
            if (_allSuites.Any())
            {
                foreach (var suite in _allSuites)
                {
                    if (suite.name == suiteName)
                    {
                        _log.Debug("Sute with name '" + suiteName + "' found, its id '" + suite.id +
                                   "' will be returned.");
                        return suite;
                    }
                }
                _log.Debug("Suite with name '" + suiteName +
                           "' was not found in the list of all suites for project with id '" + _projectId + "'.");
                _log.Debug("Trying to create it.");
                newSuite = JsonConvert.DeserializeObject<TestSute>(_apiClient.SendPost("add_suite/" + _projectId, new { name = suiteName, description = "Suite added by test rail reporter." }).ToString());
                _log.Debug("Suite created successful with id: " + newSuite.id);
                _allSuites.Add(newSuite);
                return newSuite;
            }
            _log.Debug("All suites list is empty we assume the project don't have any suites so we are going to create a new suite.");
            newSuite = JsonConvert.DeserializeObject<TestSute>(_apiClient.SendPost("add_suite/" + _projectId, new { name = suiteName, description = "Suite added by test rail reporter." }).ToString());
            _log.Debug("Suite created successful with id: " + newSuite.id);
            _allSuites.Add(newSuite);
            return newSuite;
        }

        public TestCase CreateNewTestCase(string suiteid, string testCaseName, string sectionName, List<string> steps)
        {
            _log.Debug("Extracting all section from suite with id: " + suiteid);
            var sections = JsonConvert.DeserializeObject<List<Section>>(_apiClient.SendGet("get_sections/" + _projectId + "&suite_id=" + suiteid).ToString());
            _log.Debug(sections.Count + " section retrieved.");
            string sectionId;
            if (sections.Count == 0)
            {
                _log.Debug("There are no sections for suite with id '" + suiteid + "'!");
                _log.Debug("Trying to create new section");
                var section = JsonConvert.DeserializeObject<Section>(_apiClient.SendPost("add_section/" + _projectId, new { description = "Default section", suite_id = suiteid, name = "Default section", }).ToString());
                _log.Debug("Section created successful with id: " + section.id);
                sectionId = section.id;
            }
            else
            {
                _log.Debug("Finding section id by section name '" + sectionName + "'");
                sectionId = sections.Where(section => section.name == sectionName).Select(section => section.id).FirstOrDefault();
                _log.Debug("Retrieved ids is: " + sectionId);
            }
            if (string.IsNullOrEmpty(sectionId))
            {
                _log.Debug("Id is empty so setting it the id of the first section: " + sections.First().id);
                sectionId = sections.First().id;
            }
            _log.Debug("Trying to create test case with name '" + testCaseName + "'");
            var scenarioSteps = new List<TestCaseStep>();
            if (steps != null && steps.Any())
            {
                scenarioSteps.AddRange(steps.Select(step => new TestCaseStep(step, "")));
                var testCase = JsonConvert.DeserializeObject<TestCase>(_apiClient.SendPost("add_case/" + sectionId, new { title = testCaseName, type_id = 2, priority_id = 5, custom_steps_separated = scenarioSteps }).ToString());
                _log.Debug("Test case created successful with case id: " + testCase.id);
                _allTestCases.Add(testCase);
                return testCase;
            }
            else
            {
                var testCase = JsonConvert.DeserializeObject<TestCase>(_apiClient.SendPost("add_case/" + sectionId, new { title = testCaseName, type_id = 2, priority_id = 5 }).ToString());
                _log.Debug("Test case created successful with case id: " + testCase.id);
                _allTestCases.Add(testCase);
                return testCase;
            }
        }

        public void LoadAllTestCasesForProject()
        {
            _allTestCases.Clear();
            if (!_allSuites.Any())
            {
                throw new Exception("There are currently no suites loaded, this mean that current project don't have suites or load suites method is not called!");
            }
            foreach (var sute in _allSuites)
            {
                _log.Debug("Current suit id is: " + sute.id);
                _log.Debug("Retrieving all test case for it.");
                var cases = JsonConvert.DeserializeObject<List<TestCase>>(_apiClient.SendGet("get_cases/" + _projectId + "&suite_id=" + sute.id).ToString());
                _log.Debug(cases.Count + " cases retrieved.");
                foreach (var testRailTestCaseObj in cases)
                {
                    _allTestCases.Add(testRailTestCaseObj);
                }
            }
        }

        public TestCase RetriveCaseIdByScenarioName(string scenarioName)
        {
            if (_allTestCases.Any())
            {
                foreach (var testCase in _allTestCases)
                {
                    if (testCase.title == scenarioName)
                    {
                        _log.Debug("Test case found with id: " + testCase.id);
                        return testCase;
                    }
                }
            }
            _log.Debug("Test case not found returning null");
            return null;
        }

        public List<ConfigurationGroup> RetriveConfigurationsForProject()
        {
            _log.Debug("Extracting all configuration from project with id: " + _projectId);
            var testRailConfigs = JsonConvert.DeserializeObject<List<ConfigurationGroup>>(_apiClient.SendGet("get_configs/" + _projectId).ToString());
            if (testRailConfigs.Any())
            {
                _log.Debug(testRailConfigs.Count + " configurations found.");
                return testRailConfigs;
            }
            _log.Debug("Configurations not found returning null");
            return new List<ConfigurationGroup>();
        }

        public void LoadTestRailConfigurations()
        {
            _allConfigurations = RetriveConfigurationsForProject();
        }

        public string ReturnConfigurationId(string configurationName)
        {
            foreach (var configurationGroup in _allConfigurations)
            {
                foreach (var config in configurationGroup.configs.Where(config => configurationName == config.name))
                {
                    return config.id;
                }
            }
            _log.Info("Configuration with name '" + configurationName + "' can not be found returning null.");
            return null;
        }

        public PlanEntry AddPlanEntryToTestPlan(string planId, PlanEntry palnEntry)
        {
            _log.Debug("Adding plan entry to project with id '" + _projectId + "' and test plan with id '" + planId + "'");
            var newPlanEntry = JsonConvert.DeserializeObject<PlanEntry>(_apiClient.SendPost("add_plan_entry/" + planId, palnEntry).ToString());
            _log.Debug("New plan entry with id '" + newPlanEntry.id + "' was created successful.");
            return newPlanEntry;
        }

        public void Report()
        {
            if (_data == null)
            {
                throw new Exception("The data object which needs to contains information that will be send to test rail is null!");
            }
            var parsedData = new DataParser().Parse(_data);
            _log.Info("Retrieving test plan from file store.");
            var testPlan = new FileStoreIteractions(parsedData.FileStore).GetPlanFromFileStore();
            _log.Info("Plan with id '" + testPlan.id + "' retrieved successful.");
            _log.Info("Retrieving test suite for current scenario.");
            LoadAllSuitesForProject();
            LoadAllTestCasesForProject();
            var suteId = "";
            foreach (var testCase in _allTestCases)
            {
                if (testCase.title == parsedData.ScenarioName)
                {
                    suteId = testCase.suite_id;
                    break;
                }
            }
            if (string.IsNullOrEmpty(suteId))
            {
                throw new Exception("Suite id can not be retrieved!");
            }
            _log.Info("Suite id '" + suteId + "' retrieved successful.");
            var runs = new List<TestRun>();
            LoadTestRailConfigurations();
            foreach (var entry in testPlan.entries)
            {
                runs.AddRange(entry.runs);
            }
            if (runs.Any())
            {
                var newReult = AddResultForTest(parsedData, runs, suteId);
                if (newReult != null)
                {
                    _log.Info("Result added successful for test case with id " + newReult.case_id);
                }
                else
                {
                    _log.Warn("Result can not be added!");
                }
            }
            else
            {
                throw new Exception("Runs for current test plan with id '" + testPlan.id + "' can not be retrieved!");
            }
        }

        public Result AddResultForTest(TestCaseExecutionData parsedData, IEnumerable<TestRun> runs, string suteId)
        {
            _log.Info("Current test plan have runs so searching for scenario with name '" + parsedData.ScenarioName + "' and configurations '" + parsedData.Configurations + "' in the list of runs.");
            var runId = "";
            if (!string.IsNullOrEmpty(parsedData.Configurations))
            {
                var configs = parsedData.Configurations.Split(new[] { " : " }, StringSplitOptions.None);
                var configIds = new List<string>();
                _log.Info("Trying to find all config id for current scenario.");
                foreach (var config in configs)
                {
                    var configId = ReturnConfigurationId(config);
                    if (configId == null)
                    {
                        throw new Exception("Project with id '' probably don't have any configurations or load configuration method is not called!");
                    }
                    _log.Info("Adding id '" + configId + "' to the list of config id's.");
                    configIds.Add(configId);
                }
                _log.Info(configIds.Count + " configuration id's found.");
                _log.Info("Trying to find run id for current scenario with specified configurations.");

                foreach (var testRun in runs.Where(testRun => testRun.config_ids.SequenceEqual(configIds) && testRun.suite_id == suteId))
                {
                    runId = testRun.id;
                }
            }
            else
            {
                _log.Info("Trying to find run id for current scenario with specified configurations.");

                foreach (var testRun in runs.Where(testRun => testRun.suite_id == suteId))
                {
                    runId = testRun.id;
                }
            }
            if (string.IsNullOrEmpty(runId))
            {
                throw new Exception("Run id can not be found!");
            }
            _log.Info("Run id '" + runId + "' found successful.");
            _log.Info("Trying to find test case id for current scenario.");
            var tests = RetriveTests(runId);
            var testCaseId = "";
            foreach (var test in tests)
            {
                if (test.title == parsedData.ScenarioName)
                {
                    testCaseId = test.case_id;
                }
            }
            if (string.IsNullOrEmpty(testCaseId))
            {
                throw new Exception("Test case id can not be retrieved!");
            }
            _log.Info("Test case id '" + testCaseId + "' found.");
            _log.Info("Trying to update the status of relevant test case.");
            var comment = "";
            if (parsedData.Status == "failed")
            {
                var additionaData = parsedData.AdditionalData.Aggregate("", (current, line) => current + "\n" + line);
                comment = "Scenario failed becouse of error: \n\n" + parsedData.FailureStackTrace + "\n\n Screen shoot can be found here: " + parsedData.ScreenShotLocation + "\n\n Failing step is: " + parsedData.FailingStep + "\n\n" + additionaData;
            }
            var result = new Result
            {
                case_id = testCaseId,
                status_id = ParsStatus(parsedData.Status),
                comment = comment,
                defects = parsedData.KnownIssues
            };
            return AddResultForCase(result, runId, testCaseId);
        }

        public Result AddResultForCase(Result result, string runId, string caseId)
        {
            return JsonConvert.DeserializeObject<Result>(_apiClient.SendPost("add_result_for_case/" + runId + "/" + caseId, result).ToString());
        }

        public TestPlan CreateTestPlan(String name, String description)
        {
            return JsonConvert.DeserializeObject<TestPlan>(_apiClient.SendPost("add_plan/" + _projectId, new { name, description }).ToString());
        }

        public List<Test> RetriveTests(string runId)
        {
            return JsonConvert.DeserializeObject<List<Test>>(_apiClient.SendGet("get_tests/" + runId).ToString());
        }

        public string GetTestPlanAsString(String id)
        {
            return _apiClient.SendGet("get_plan/" + id).ToString();
        }

        public TestPlan CloseTestPlan(string planId)
        {
            return JsonConvert.DeserializeObject<TestPlan>(_apiClient.SendPost("close_plan/" + planId, new object()).ToString());
        }

        public List<TestCase> CreatListWithRelevantTestCaseObjects(IEnumerable<ScenarioObj> scenarious, string executionCategory)
        {
            var relevantTestCase = new List<TestCase>();
            foreach (var scenarioObj in scenarious)
            {
                var featureName = scenarioObj.FeatureName;
                if (!string.IsNullOrEmpty(executionCategory) && !scenarioObj.CategoryAttribute.Contains(executionCategory))
                {
                    _log.Info("Scenario with name '" + scenarioObj.Name + "' from feature '" + scenarioObj.FeatureName + "' is not marked for execution");
                    continue;
                }
                var testCase = RetriveCaseIdByScenarioName(scenarioObj.Name);
                if (testCase != null)
                {
                    _log.Info("Adding test case with id '" + testCase.id + "' and name '" + testCase.title +
                             "' added to the list of relevant tests.");
                    relevantTestCase.Add(testCase);
                }
                else
                {
                    _log.Info("Scenario not found in test rail so we assume it is not part of the project, we will try to add it.");
                    var suite = FindSuiteByName(featureName);
                    if (suite != null)
                    {
                        _log.Debug("Trying to add new test case with name '" + scenarioObj.Name +
                                  "' to suite with id '" + suite.id + "'.");
                        testCase = CreateNewTestCase(suite.id, scenarioObj.Name, null, null);
                        _log.Info("Adding test case with id '" + testCase.id + "' and name '" + testCase.title +
                                 "' added to the list of relevant tests.");
                        relevantTestCase.Add(testCase);
                    }
                    else
                    {
                        _log.Debug("Trying to add new suite with name '" + featureName + "'!");
                        var newSuite = CreateNewSuiteIfNotExist(featureName);
                        _log.Debug("Trying to add new test case with name '" + scenarioObj.Name +
                                  "' to already created suite!");
                        testCase = CreateNewTestCase(newSuite.id, scenarioObj.Name, null, null);
                        _log.Info("Adding test case with id '" + testCase.id + "' and name '" + testCase.title +
                                 "' added to the list of relevant tests.");
                        relevantTestCase.Add(testCase);
                    }
                }
            }
            return relevantTestCase;
        }

        public string ParsStatus(string status)
        {
            switch (status.ToLower())
            {
                case "passed":
                    {
                        return "1";
                    }
                case "blocked":
                    {
                        return "2";
                    }
                case "untested":
                    {
                        return "3";
                    }
                case "retest":
                    {
                        return "4";
                    }
                case "failed":
                    {
                        return "5";
                    }
                default:
                    throw new Exception("Provided status '" + status + "' is not supported!");
            }
        }
    }
}
