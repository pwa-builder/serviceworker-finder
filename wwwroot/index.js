const codeArea = document.querySelector("textarea");
const submitBtn = document.querySelector("#submitBtn")
const resultsDiv = document.querySelector("#results");
const spinner = document.querySelector(".spinner-border");
const tableBody = document.querySelector("table tbody");

submitBtn.addEventListener("click", () => submit());
rowErrors = [];

setCode(getDefaultSites());
codeArea.scrollTop = 0;

function setCode(options) {
    codeArea.value = options.join("\n");
    codeArea.scrollTop = 1000000;
    clearResults();
}

function clearResults() {
    tableBody.innerHTML = "";
    rowErrors = [];
}

function getDefaultSites() {
    return [
        "https://www.pwabuilder.com",
        "https://applescrusher.azurewebsites.net",
        "https://webboard.app",
        "https://messianicradio.com",
        "https://m.aliexpress.com",
        "https://m.alibaba.com",
        "https://www.trivago.in",
        "https://twitter.com",
        "https://www.pinterest.com",
        "https://web.telegram.org",
        "https://weather.com",
        "https://app.starbucks.com",
        "https://www.washingtonpost.com",
        "https://chrisdiana.dev/pwa-calculator/index.html",
        "https://www.apewebapps.com/death-3d ",
        "https://pwatictactoe.web.app/index.html ",
        "https://www.brucelawson.co.uk",
        "https://music.youtube.com ",
        "https://www.cbsnews.com/?pwa",
        "https://www.lemonde.fr"
    ];
}

async function submit() {
    clearResults();
    const sites = codeArea.value.split("\n");
    const sitesWithRows = sites.map(s => {
        return {
            site: s,
            row: createRow(s)
        }
    });
    sitesWithRows.forEach(s => tableBody.appendChild(s.row));

    setLoading(true);
    for (let siteRow of sitesWithRows) {
        setRowActive(siteRow.row);
        const startTime = new Date();
        try {
            const fetchResponse = await fetch(`/serviceworker/runAllChecks?url=${encodeURIComponent(siteRow.site)}`, {
                method: "GET",
                headers: new Headers({ 'content-type': 'application/json' }),
            });
            const duration = getDuration(startTime, new Date());

            if (!fetchResponse.ok) {
                showErrorForRow(siteRow.row, await fetchResponse.text(), duration);
            } else {
                const responseJson = await fetchResponse.json();
                if (!responseJson.hasSW) {
                    showErrorForRow(siteRow.row, responseJson.noServiceWorkerFoundDetails, duration);
                } else {
                    showSuccessForRow(siteRow.row, responseJson, duration);
                }
            }
        } catch (fetchError) {
            showErrorForRow(siteRow.row, fetchError, getDuration(startTime, new Date()));
        }
    }

    setLoading(false);
}

function showErrorForRow(row, errorDetails, duration) {
    removeRowActive(row);
    rowErrors.push(errorDetails);

    row.children[0].innerHTML = `<i class="fas fa-check"></i>`;
    row.children[2].innerHTML = `<i class="fas fa-times text-danger"></i>`;
    row.children[3].innerHTML = "";
    row.children[4].innerHTML = "";
    row.children[5].innerHTML = "";
    row.children[6].innerHTML = `<button class="btn btn-danger btn-sm" onclick="showError(${rowErrors.length - 1})">Show error</button>`;
    row.children[7].innerText = duration;
    
    console.warn("Showing error for row", row, errorDetails);
}

function showSuccessForRow(row, resultsJson, duration) {
    removeRowActive(row);
    row.children[0].innerHTML = `<i class="fas fa-check"></i>`; // success/fail cell
    row.children[2].innerHTML = `<i class="fas fa-check-circle text-success"></i><br><span>${resultsJson.url}</span>`; // sw url cell
    row.children[3].innerHTML = resultsJson.hasPushRegistration ? `<i class="fas fa-check-circle text-success"></i>` : `<i class="fas fa-times text-warning"></i>`; // push reg cell
    row.children[4].innerHTML = resultsJson.hasBackgroundSync ? `<i class="fas fa-check-circle text-success"></i>` : `<i class="fas fa-times text-warning"></i>`; // background sync cell
    row.children[5].innerHTML = resultsJson.hasPeriodicBackgroundSync ? `<i class="fas fa-check-circle text-success"></i>` : `<i class="fas fa-times text-warning"></i>`; // periodic sync cell
    row.children[6].innerText = ""; // error cell
    row.children[7].innerText = duration;
    console.info("Showing success for row", row, resultsJson);
}

function getDuration(start, end) {
    let distance = Math.abs(start - end);
    const hours = Math.floor(distance / 3600000);
    distance -= hours * 3600000;
    const minutes = Math.floor(distance / 60000);
    distance -= minutes * 60000;
    const seconds = Math.floor(distance / 1000);
    return `${hours}:${('0' + minutes).slice(-2)}:${('0' + seconds).slice(-2)}`;
}

function removeRowActive(row) {
    row.classList.remove("table-active");
    row.children[0].innerHTML = "";
}

function setRowActive(row) {
    document.querySelectorAll("table-active").forEach(el => removeRowActive(el));
    row.classList.add("table-active");
    row.children[0].innerHTML = getSpinnerHtml();
}

function createRow(site) {
    const row = document.createElement("tr");

    const statusCell = document.createElement("th");
    const siteCell = document.createElement("td");
    const swDetectedCell = document.createElement("td");
    const pushRegCell = document.createElement("td");
    const backgroundSyncCell = document.createElement("td");
    const periodicSyncCell = document.createElement("td");
    const errorDetailsCell = document.createElement("td");
    const durationCell = document.createElement("td");

    siteCell.innerText = site;

    row.append(statusCell, siteCell, swDetectedCell, pushRegCell, backgroundSyncCell, periodicSyncCell, errorDetailsCell, durationCell);
    return row;
}

function getSpinnerHtml() {
    return `<div class="spinner-border" role="status"></div>`;
}

function setLoading(state) {
    submitBtn.disabled = state;
    if (state) {
        spinner.classList.remove("d-none");
    } else {
        spinner.classList.add("d-none");
    }
}

function showError(errorIndex) {
    const error = rowErrors[errorIndex];
    const errorModal = new bootstrap.Modal(document.querySelector('.modal'), {
        keyboard: false
    });
    document.querySelector(".modal-body pre").innerText = error;
    errorModal.show();
}